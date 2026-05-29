import json
import uuid
import chromadb
from sentence_transformers import SentenceTransformer

# ========== 配置（与主程序一致） ==========
KNOWLEDGE_DB_DIR = "./memory_db/knowledge"     # 知识库目录
EMBEDDING_MODEL_PATH = './shibing624_text2vec-base-chinese/zjwan461/shibing624_text2vec-base-chinese'
COLLECTION_NAME = "knowledge_base"
BACKUP_FILE = "knowledge.json"          # 备份文件

def main():
    # 加载嵌入模型
    print("加载嵌入模型...")
    model = SentenceTransformer(EMBEDDING_MODEL_PATH)

    # 连接知识库
    client = chromadb.PersistentClient(path=KNOWLEDGE_DB_DIR)

    # 删除旧集合（清空所有旧数据）
    try:
        client.delete_collection(COLLECTION_NAME)
        print(f"已删除旧集合: {COLLECTION_NAME}")
    except:
        print(f"集合 {COLLECTION_NAME} 不存在，无需删除")

    # 创建新集合
    collection = client.create_collection(COLLECTION_NAME, metadata={"hnsw:space": "cosine"})
    print(f"已创建新集合: {COLLECTION_NAME}")

    # 读取备份 JSON 文件
    try:
        with open(BACKUP_FILE, "r", encoding="utf-8") as f:
            data = json.load(f)
    except FileNotFoundError:
        print(f"备份文件 {BACKUP_FILE} 不存在，退出。")
        return
    except json.JSONDecodeError:
        print(f"备份文件 {BACKUP_FILE} 格式错误，退出。")
        return

    if not isinstance(data, list) or len(data) == 0:
        print("备份文件中没有有效知识条目，退出。")
        return

    # 准备数据
    contents = []
    ids = []
    for content in data:
        if isinstance(content, str) and content.strip():
            contents.append(content)
            ids.append(f"kb_{uuid.uuid4().hex}")

    if not contents:
        print("备份文件中没有有效知识条目，退出。")
        return

    # 向量化并批量存入
    print(f"正在向量化 {len(contents)} 条知识...")
    vectors = model.encode(contents).tolist()
    collection.add(
        embeddings=vectors,
        documents=contents,
        ids=ids
    )
    print(f"✅ 成功导入 {len(contents)} 条知识到集合 {COLLECTION_NAME}")

if __name__ == "__main__":
    main()