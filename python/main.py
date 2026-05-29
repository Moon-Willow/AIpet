import asyncio
import json
import re
from datetime import datetime, date
from langchain_openai import ChatOpenAI
from langchain_core.messages import HumanMessage, AIMessage, ToolMessage, SystemMessage
from langchain_mcp_adapters.client import MultiServerMCPClient
from fastapi import FastAPI, HTTPException
from fastapi.responses import FileResponse
from fastapi.middleware.cors import CORSMiddleware
import uvicorn
from pydantic import BaseModel
from langchain_core.prompts import ChatPromptTemplate, MessagesPlaceholder
from langchain_core.runnables.history import RunnableWithMessageHistory
from langchain_community.chat_message_histories import ChatMessageHistory
import uuid
import os
import chromadb
from sentence_transformers import SentenceTransformer
import importlib
import pkgutil
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

# 尝试导入 plugins 包，如果不存在则提示用户创建
try:
    import plugins
except ImportError:
    print("错误：未找到 plugins 目录，请确保项目根目录存在 plugins/__init__.py")
    exit(1)

from plugins.base import BasePlugin

# ======================= 基本配置 =========================
MUSIC_DIR = "./music"
VOICE_DIR = "./sing/voice"
BGM_DIR = "./sing/bgm"
os.makedirs(MUSIC_DIR, exist_ok=True)
os.makedirs(VOICE_DIR, exist_ok=True)
os.makedirs(BGM_DIR, exist_ok=True)

SERVER_HOST = os.getenv("SERVER_HOST", "127.0.0.1")
SERVER_PORT = 9000
MAX_HISTORY = 30               # 上下文窗口 30 轮
SUMMARY_ROUND = 30             # 每 30 轮生成一次摘要
# 加载配置
config_all = json.load(open('config.json', encoding='utf-8'))
model_data = config_all['model']
mcp_data = config_all['MCP']

# ======================= 摘要变量列表与轮次计数 =======================
SUMMARY_VARIABLES = []          # 最多 3 条
MAX_SUMMARIES = 3
ROUND_COUNT = 0
ROUND_LOCK = asyncio.Lock()
summary_file_lock = asyncio.Lock()  # 保护 JSON 文件写入

# ======================= ChromaDB 与嵌入模型 ===================
# 知识库数据库路径
KNOWLEDGE_DB_DIR = "./memory_db/knowledge"
os.makedirs(KNOWLEDGE_DB_DIR, exist_ok=True)
# 摘要记忆库数据库路径
SUMMARY_DB_DIR = "./memory_db/summary"
os.makedirs(SUMMARY_DB_DIR, exist_ok=True)

# 嵌入模型路径（请根据实际调整）
EMBEDDING_MODEL_PATH = './shibing624_text2vec-base-chinese/zjwan461/shibing624_text2vec-base-chinese'
embedding_model = SentenceTransformer(EMBEDDING_MODEL_PATH)

# 知识库客户端与集合
kb_chroma_client = chromadb.PersistentClient(path=KNOWLEDGE_DB_DIR)
# 摘要记忆库客户端与集合
summary_chroma_client = chromadb.PersistentClient(path=SUMMARY_DB_DIR)

KNOWLEDGE_COLLECTION = "knowledge_base"
SUMMARY_COLLECTION = "summary_memory"

def get_or_create_collection(client, name):
    try:
        return client.get_collection(name)
    except:
        return client.create_collection(name, metadata={"hnsw:space": "cosine"})

knowledge_col = get_or_create_collection(kb_chroma_client, KNOWLEDGE_COLLECTION)
summary_col = get_or_create_collection(summary_chroma_client, SUMMARY_COLLECTION)

# ======================= 通用检索函数（带阈值） ==================
def retrieve_with_threshold(collection, query: str, k: int, threshold: float):
    if collection.count() == 0:
        return []
    query_vec = embedding_model.encode(query).tolist()
    results = collection.query(
        query_embeddings=[query_vec],
        n_results=k,
        include=["documents", "distances"]
    )
    if not results['documents'] or not results['documents'][0]:
        return []
    filtered = []
    for doc, dist in zip(results['documents'][0], results['distances'][0]):
        similarity = 1 - dist
        if similarity >= threshold:
            filtered.append(doc)
    return filtered

# ======================= 滑动窗口记忆管理（30 轮） =======================
class WindowChatMessageHistory(ChatMessageHistory):
    def add_message(self, message):
        super().add_message(message)
        max_messages = MAX_HISTORY * 2
        while len(self.messages) > max_messages:
            self.messages.pop(0)

_global_history = WindowChatMessageHistory()
def get_session_history(session_id: str):
    return _global_history

# ======================= 插件管理器 =======================
class PluginManager:
    def __init__(self):
        self.plugins = {}
    def discover_plugins(self):
        package = plugins
        for _, module_name, _ in pkgutil.iter_modules(package.__path__):
            if module_name.startswith('_') or module_name == 'base':
                continue
            full_module = f"plugins.{module_name}"
            module = importlib.import_module(full_module)
            for attr_name in dir(module):
                cls = getattr(module, attr_name)
                if isinstance(cls, type) and issubclass(cls, BasePlugin) and cls != BasePlugin:
                    instance = cls()
                    self.plugins[cls.__name__] = instance
                    print(f"已发现插件: {cls.__name__}")
    def load_all(self, config: dict):
        self.discover_plugins()
        for plugin in self.plugins.values():
            plugin.configure(config)
    def execute_hook(self, hook_name: str, *args, **kwargs):
        if hook_name in ("on_user_input", "on_assistant_output_before"):
            text = args[0] if args else ""
            for plugin in self.plugins.values():
                if plugin.enabled:
                    func = getattr(plugin, hook_name, None)
                    if func:
                        text = func(text) or text
            return text
        results = {}
        for plugin in self.plugins.values():
            if plugin.enabled:
                func = getattr(plugin, hook_name, None)
                if func:
                    res = func(*args, **kwargs)
                    if isinstance(res, dict):
                        results.update(res)
        return results
    def enable(self, name: str):
        if name in self.plugins:
            self.plugins[name].enabled = True
    def disable(self, name: str):
        if name in self.plugins:
            self.plugins[name].enabled = False
    def get_status(self):
        return {name: p.enabled for name, p in self.plugins.items()}
    def configure_plugin(self, name: str, new_config: dict):
        if name in self.plugins:
            self.plugins[name].config.update(new_config)
            if "enabled" in new_config:
                self.plugins[name].enabled = new_config["enabled"]

plugin_manager = PluginManager()
plugin_manager.load_all(config_all)

# ======================= MCP 工具 ==========================
async def get_tool():
    client = MultiServerMCPClient(mcp_data)
    return await client.get_tools()

async def use_tool(tool_list, tool_name, args):
    for tool in tool_list:
        if tool.name == tool_name:
            return await tool.ainvoke(args)
    return None

# ======================= 对话日志保存 ==========================
CHAT_LOG_DIR = "./chat_logs"
os.makedirs(CHAT_LOG_DIR, exist_ok=True)
save_lock = asyncio.Lock()

async def save_conversation(user_text: str, assistant_text: str):
    today = date.today().isoformat()
    file_path = os.path.join(CHAT_LOG_DIR, f"chat_{today}.json")
    record = {"user": user_text, "assistant": assistant_text or ""}
    async with save_lock:
        loop = asyncio.get_running_loop()
        try:
            data = await loop.run_in_executor(None, _read_json_file, file_path)
        except (FileNotFoundError, json.JSONDecodeError):
            data = []
        data.append(record)
        await loop.run_in_executor(None, _write_json_file, file_path, data)

def _read_json_file(path: str):
    with open(path, "r", encoding="utf-8") as f:
        return json.load(f)

def _write_json_file(path: str, data):
    with open(path, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=2)

# ======================= TTS 文本清洗 =======================
def clean_tts_text(text: str) -> str:
    """
    清洗文本，只保留适合 TTS 朗读的部分，支持中文、日语、英文及数字。
    - 删除 *...*、_..._ 包裹的动作/表情描写
    - 删除中文/英文括号及其中的内容
    - 保留所有常见中日英文字符及标点
    - 合并多余空白
    """
    if not text:
        return text

    # 1. 删除动作描写
    text = re.sub(r'[*_].*?[*_]', '', text)

    # 2. 删除括号及其中的内容（全角“（）”和半角“()”）
    text = re.sub(r'[（(][^）)]*[）)]', '', text)

    # 3. 保留字符集（白名单）
    #   - 英文字母：a-zA-Z
    #   - 数字：0-9
    #   - 中文汉字（含日文汉字）：\u4e00-\u9fff (扩展区，足够)
    #   - 平假名：\u3040-\u309f
    #   - 片假名：\u30a0-\u30ff (含长音符「ー」\u30fc)
    #   - 半角片假名：\uFF65-\uFF9F
    #   - 日文标点与符号：\u3000-\u303f (、。「」〼 etc.)
    #   - 日文重复标记与特殊符号：
    #       \u3005  々
    #       \u3006  〆
    #       \u309d-\u309e  ゝゞ
    #       \u30fd-\u30fe  ヽヾ
    #   - 其他常用符号：※ \u203b, ‥ \u2025, ～ \uff5e (已在半角片假名块)
    #   - 中文标点：，。！？、：：；""''…
    #   - 空格
    keep_pattern = (
        r'[^a-zA-Z0-9'
        r'\u4e00-\u9fff'          # 中日韩统一表意文字
        r'\u3040-\u309f'          # 平假名
        r'\u30a0-\u30ff'          # 片假名
        r'\uff65-\uff9f'          # 半角片假名
        r'\u3000-\u303f'          # 日文标点、符号
        r'\u3005\u3006'           # 々, 〆
        r'\u309d-\u309e'          # ゝ, ゞ
        r'\u30fd-\u30fe'          # ヽ, ヾ
        r'\u203b\u2025'           # ※, ‥
        r'\uff5e'                 # ～ (全角波浪号)
        r'，。！？、：：；""''…'   # 中文标点
        r'\s]'                    # 空白字符
    )
    text = re.sub(keep_pattern, '', text)

    # 4. 合并连续空白并去除首尾
    text = re.sub(r'\s+', ' ', text).strip()

    return text

# ======================= LLM 压缩摘要 =======================
async def generate_summary_from_messages(history: ChatMessageHistory) -> str:
    messages = history.messages[-60:]
    if not messages:
        return ""
    conversation_text = ""
    for msg in messages:
        if isinstance(msg, HumanMessage):
            conversation_text += f"用户: {msg.content}\n"
        elif isinstance(msg, AIMessage):
            conversation_text += f"桌宠: {msg.content}\n"
        else:
            continue
    if not conversation_text:
        return ""

    compress_prompt = f"""请将以下对话片段压缩成一段简短的摘要（中文，不超过100字），只保留关键事实和事件，其中assistant是桌宠：
对话：
{conversation_text}
摘要："""
    if not llm:
        return ""
    try:
        res = await asyncio.to_thread(llm.invoke, compress_prompt)
        if isinstance(res, str):
            return res.strip()
        elif hasattr(res, 'content'):
            return res.content.strip()
        else:
            return str(res)
    except Exception as e:
        print(f"摘要生成失败: {e}")
        return ""

# ======================= FastAPI 实例 ===========================
class Query(BaseModel):
    query: str

app = FastAPI()
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

llm = None
tools = None

@app.post('/chat')
async def chat(query: Query):
    global tools, llm, model_data, ROUND_COUNT
    response = {
        "llmres": None,
        "tts_text": None,
        "music_url": None, "music_type": None,
        "voice_url": None, "voice_type": None,
        "bgm_url": None, "bgm_type": None,
        "type": None, "emotion": None
    }

    if not llm:
        if model_data['modelname'] and model_data['base_url']:
            if not model_data['istool']:
                llm = ChatOpenAI(
                    model=model_data['modelname'],
                    base_url=model_data['base_url'],
                    api_key=model_data.get('api_key', ''),
                    extra_body=model_data.get('extra_body', {})
                )
            else:
                tools = await get_tool()
                llm = ChatOpenAI(
                    model=model_data['modelname'],
                    base_url=model_data['base_url'],
                    api_key=model_data.get('api_key', ''),
                    extra_body=model_data.get('extra_body', {})
                ).bind_tools(tools)

    # 用户输入插件
    user_text = query.query
    user_text = plugin_manager.execute_hook("on_user_input", user_text)
    extra_user_fields = plugin_manager.execute_hook("on_user_input_after", user_text)
    response.update(extra_user_fields)

    # 记忆检索
    knowledges = retrieve_with_threshold(knowledge_col, user_text, k=2, threshold=0.2)
    summary_mems = retrieve_with_threshold(summary_col, user_text, k=2, threshold=0.2)

    # 构建系统提示词
    core_prompt = model_data['prompt']
    prompt_blocks = [core_prompt]
    global SUMMARY_VARIABLES
    if SUMMARY_VARIABLES:
        prompt_blocks.append("【近期对话摘要】\n" + "\n".join(f"- {s}" for s in SUMMARY_VARIABLES))
    if knowledges:
        prompt_blocks.append("【可能与当前对话有关的相关信息】\n" + "\n".join(f"- {k}" for k in knowledges))
    if summary_mems:
        prompt_blocks.append("【可能与当前对话有关的你与用户的相关历史记忆摘要】\n" + "\n".join(f"- {m}" for m in summary_mems))

    system_content = "\n\n".join(prompt_blocks)

    prompt = ChatPromptTemplate.from_messages([
        SystemMessage(content=system_content),
        MessagesPlaceholder(variable_name="history"),
        ("human", "{input}"),
    ])

    chain = prompt | llm
    chain_with_history = RunnableWithMessageHistory(
        chain,
        get_session_history,
        input_messages_key="input",
        history_messages_key="history",
    )
    config = {"configurable": {"session_id": "default"}}

    res = chain_with_history.invoke({"input": user_text}, config=config)

    # 工具调用处理
    if res.tool_calls:
        tool_call = res.tool_calls[0]
        result = await use_tool(tool_list=tools, tool_name=tool_call['name'], args=tool_call['args'])
        print(result)
        try:
            tool_data = eval(result[0]['text'])
            if tool_call['name'] == 'music_play':
                response['music_url'] = tool_data.get('music_url', '')
                response['music_type'] = tool_data.get('music_type', '')
                response['type'] = tool_data.get('type', '')
            elif tool_call['name'] == 'sing':
                response['voice_url'] = tool_data.get('voice_url', '')
                response['voice_type'] = tool_data.get('voice_type', '')
                response['bgm_url'] = tool_data.get('bgm_url', '')
                response['bgm_type'] = tool_data.get('bgm_type', '')
                response['type'] = tool_data.get('type', '')
        except Exception as e:
            print(f"解析工具返回数据出错: {e}")

        history = get_session_history("default")
        history.add_message(ToolMessage(content=result, tool_call_id=tool_call['id']))
        res = chain_with_history.invoke({"input": ""}, config=config)

    assistant_text = res.content

    # AI 回复插件
    assistant_text = plugin_manager.execute_hook("on_assistant_output_before", assistant_text)
    response['llmres'] = assistant_text

    tts_cleaned = clean_tts_text(assistant_text)
    response['tts_text'] = tts_cleaned if tts_cleaned else assistant_text

    plugin_global_config = {
        "server_host": SERVER_HOST,
        "server_port": SERVER_PORT,
    }
    extra_fields = plugin_manager.execute_hook("on_assistant_output",
                                               text=assistant_text,
                                               global_config=plugin_global_config)
    response.update(extra_fields)

    # 保存对话日志
    user_text_original = query.query
    try:
        await save_conversation(user_text_original, assistant_text)
    except Exception as e:
        print(f"保存日志失败：{e}")

    # 轮次计数与摘要触发
    async with ROUND_LOCK:
        ROUND_COUNT += 1
        if ROUND_COUNT % SUMMARY_ROUND == 0:
            asyncio.create_task(handle_summary_generation())

    print(assistant_text)
    return response

async def handle_summary_generation():
    """后台任务：生成摘要并立即写入摘要记忆库和JSON备份文件，同时更新内存列表"""
    global SUMMARY_VARIABLES
    history = get_session_history("default")
    summary_text = await generate_summary_from_messages(history)
    if not summary_text:
        return

    now_str = datetime.now().strftime("%Y-%m-%d %H:%M")
    timed_summary = f"[{now_str}] {summary_text}"

    # 1. 存入 ChromaDB
    vec = embedding_model.encode(timed_summary).tolist()
    summary_col.add(
        embeddings=[vec],
        documents=[timed_summary],
        ids=[str(uuid.uuid4())]
    )
    print(f"新摘要已写入摘要记忆库：{timed_summary[:60]}...")

    # 2. 写入 JSON 备份文件（带锁，线程池安全）
    async with summary_file_lock:
        summary_file = "summary.json"
        def write_json():
            data = []
            if os.path.exists(summary_file):
                try:
                    with open(summary_file, "r", encoding="utf-8") as f:
                        data = json.load(f)
                except:
                    pass
            data.append(timed_summary)
            with open(summary_file, "w", encoding="utf-8") as f:
                json.dump(data, f, ensure_ascii=False, indent=2)
        await asyncio.to_thread(write_json)

    # 3. 加入内存列表
    SUMMARY_VARIABLES.append(timed_summary)
    if len(SUMMARY_VARIABLES) > MAX_SUMMARIES:
        oldest = SUMMARY_VARIABLES.pop(0)
        print(f"一条旧摘要已从内存中移除：{oldest[:50]}...")

# ======================= 插件管理 API ==========================
@app.post("/plugins/reload")
async def reload_plugins():
    global plugin_manager
    plugin_manager = PluginManager()
    plugin_manager.load_all(config_all)
    return {"status": "reloaded", "plugins": list(plugin_manager.plugins.keys())}

# ======================= 静态文件服务 ==========================
@app.get("/music/{index}")
async def serve_music(index: int):
    with open('./music/name_list.json', encoding='utf-8') as f:
        json_data = json.load(f)
        value = index
        filename = next(k for k, v in json_data.items() if v == value)
    filepath = os.path.join(MUSIC_DIR, filename)
    if not os.path.exists(filepath):
        return {"error": "音频文件不存在"}
    return FileResponse(filepath, media_type=f"audio/{filename.split('.')[-1]}", filename=filename)

@app.get("/sing/voice/{index}")
async def serve_sing_voice(index: int):
    with open('./sing/name_list.json', encoding='utf-8') as f:
        json_data = json.load(f)
        filename = next(k for k, v in json_data.items() if v == index)
    filepath = os.path.join(VOICE_DIR, filename)
    if not os.path.exists(filepath):
        return {"error": "音频文件不存在"}
    return FileResponse(filepath, media_type=f"audio/{filename.split('.')[-1]}", filename=filename)

@app.get("/sing/bgm/{index}")
async def serve_sing_bgm(index: int):
    with open('./sing/name_list.json', encoding='utf-8') as f:
        json_data = json.load(f)
        filename = next(k for k, v in json_data.items() if v == index)
    filepath = os.path.join(BGM_DIR, filename)
    if not os.path.exists(filepath):
        return {"error": "音频文件不存在"}
    return FileResponse(filepath, media_type=f"audio/{filename.split('.')[-1]}", filename=filename)

if __name__ == "__main__":
    uvicorn.run(app, host='127.0.0.1', port=SERVER_PORT)