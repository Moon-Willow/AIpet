from mcp.server.fastmcp import FastMCP
import asyncio
from pathlib import Path
from rapidfuzz import fuzz, process
import json
import random
import re
# 创建 FastMCP 实例
# stateless_http=True: 无状态模式，适合多节点部署
# json_response=True: 返回 JSON 而非 SSE 流，兼容性更好
mcp = FastMCP(
    name="MCPServer",
    stateless_http=True,      # 无状态模式（推荐用于生产）
    #json_response=True,       # JSON 响应模式（非流式）
    streamable_http_path="/mcp"  # HTTP 端点路径
)

#===========歌曲库===========#
class MusicLibrary:
    def __init__(self, music_dir: str = "./music"):
        self.music_dir = Path(music_dir).resolve()
        self.music_dir.mkdir(parents=True, exist_ok=True)
        self.songs = []
        self.index = []
        self._scan_library()

    def _normalize(self, text: str) -> str:
        if not text:
            return ""
        text = text.lower().strip()
        text = re.sub(r"[\s\-_]+", "", text)
        text = re.sub(r"[()（）【】\[\]]", "", text)
        return text

    def _parse_filename(self, filename: str) -> tuple:
        name = Path(filename).stem
        parts = re.split(r"\s*[-—]\s*", name, maxsplit=1)
        if len(parts) == 2:
            return parts[0].strip(), parts[1].strip(), name
        return name.strip(), "", name

    def _scan_library(self):
        self.songs = []
        self.index = []

        for f in self.music_dir.iterdir():
            if f.is_file() and f.suffix.lower() in (".mp3", ".wav", ".flac", ".m4a", ".ogg"):
                title, artist, raw_name = self._parse_filename(f.name)

                song_data = {
                    "filename": f.name,
                    "title": title,
                    "artist": artist,
                    "raw_name": raw_name,
                    "path": str(f),
                    "ext": f.suffix.lower(),
                }
                self.songs.append(song_data)

                norm_title = self._normalize(title)
                norm_artist = self._normalize(artist)
                norm_full = self._normalize(raw_name)

                self.index.extend([
                    (norm_title, song_data, "title"),
                    (norm_artist, song_data, "artist"),
                    (norm_full, song_data, "full"),
                    (self._normalize(f"{artist}{title}"), song_data, "combined"),
                    (self._normalize(f"{title}{artist}"), song_data, "combined"),
                ])

    def match(self, music: str = "", singer: str = "", top_k: int = 3, threshold: int = 30) -> list:
        if not self.songs:
            return []

        if not music or not music.strip():
            song = random.choice(self.songs)
            return [(song, 100, "random")]

        query_music = self._normalize(music)
        query_singer = self._normalize(singer) if singer else ""
        candidates = {}

        # 歌名匹配
        for idx_text, song_data, idx_type in self.index:
            if idx_type in ("title", "full"):
                score = fuzz.ratio(query_music, idx_text)
                if idx_type == "title" and score == 100:
                    score = 120
                elif idx_type == "title" and score >= 80:
                    score += 15

                key = song_data["filename"]
                if score >= threshold and (key not in candidates or candidates[key][1] < score):
                    candidates[key] = (song_data, min(score, 100), "title_match")

        # 歌手匹配
        if query_singer:
            for idx_text, song_data, idx_type in self.index:
                if idx_type == "artist":
                    score = fuzz.ratio(query_singer, idx_text)
                    if score >= threshold:
                        key = song_data["filename"]
                        if key in candidates:
                            old_score = candidates[key][1]
                            new_score = min(old_score + score * 0.3, 100)
                            candidates[key] = (song_data, new_score, "title_artist_match")
                        else:
                            candidates[key] = (song_data, score * 0.5, "artist_match")

        # 部分匹配
        for idx_text, song_data, idx_type in self.index:
            score = fuzz.partial_ratio(query_music, idx_text)
            if score >= threshold:
                key = song_data["filename"]
                if key not in candidates or candidates[key][1] < score:
                    candidates[key] = (song_data, score, "partial_match")

        results = sorted(candidates.values(), key=lambda x: x[1], reverse=True)
        return results[:top_k]

library = MusicLibrary("./music")
singlibrary = MusicLibrary("./sing/voice")

#===========MCP工具===========
@mcp.tool(description='在百度百科中搜索相关内容，你应该告诉用户信息来自百度百科。')
async def search(Query:str)->str:
    import re
    import urllib.request
    import urllib.parse
    from bs4 import BeautifulSoup
    def query(content):
        try:
            # 请求地址
            url = 'https://baike.baidu.com/item/' + urllib.parse.quote(content)
            # 请求头部
            headers = {
                'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/67.0.3396.99 Safari/537.36'
            }
            # 利用请求地址和请求头部构造请求对象
            req = urllib.request.Request(url=url, headers=headers, method='GET')
            # 发送请求，获得响应
            response = urllib.request.urlopen(req)
        except:
            return '请求错误，请检查输入参数是否有效'
        if response.getcode() == 200:
            # 读取响应，获得文本
            text = response.read().decode('utf-8')
            soup = BeautifulSoup(text, 'html.parser')
            pattern = re.compile('lemmaSummary_.*?')
            text = soup.find('div', class_=pattern)
            pattern = r'\[\d+\]'
            result = re.sub(pattern, '', text.get_text())
            return result
        else:
            return '未查询到相关内容'

    text = query(Query)
    return text

@mcp.tool(description='当用户要求你播放音乐时，你应该优先执行此工具，如果用户没有指定哪首音乐，则music为空，其中singer参数可以为空，根据用户的话来判断该参数，最后只需要告诉用户播放了哪首音乐，不需要告诉url和music_type这两个参数的任何信息')
async def music_play(music: str = "", singer: str = "") -> dict:
    """
    播放音乐工具。在 ./music 目录下模糊匹配歌曲。
    """
    library._scan_library()
    results = library.match(music, singer, top_k=1, threshold=30)

    if not results:
        return {
            "music_name": "未找到匹配的音乐",
            "music_url": "",
            "music_type": "",
            "type": "play"
        }

    song_data, score, match_type = results[0]

    if song_data["artist"]:
        display_name = f"{song_data['title']} - {song_data['artist']}"
    else:
        display_name = song_data["title"]
    with open('./music/name_list.json',encoding='utf-8') as f:
        json_data = json.load(f)

        file_url = f"/music/{json_data[song_data['filename']]}"

    return {
        "music_name": display_name,
        "music_url":'http://127.0.0.1:9000'+file_url,
        "music_type":song_data["ext"].lstrip('.').lower(),
        "type": "play"
    }
@mcp.tool(description='当用户要求你唱歌时，你应该优先执行此功能，如果用户没有指定哪首音乐，则music为空，最后只需要告诉用户你要唱哪首音乐，不需要告诉url和music_type这两个参数的任何信息')
async def sing(music: str = "") -> dict:

    singlibrary._scan_library()
    results = singlibrary.match(music,top_k=1, threshold=30)

    if not results:
        return {
            "music_name": "未找到匹配的音乐",
            "bgm_url": "",
            "bgm_type": "",
            "voice_url":"",
            "voice_type": "",
            "type": "sing"
        }

    song_data, score, match_type = results[0]

    if song_data["artist"]:
        display_name = f"{song_data['title']} - {song_data['artist']}"
    else:
        display_name = song_data["title"]
    with open('./sing/name_list.json',encoding='utf-8') as f:
        json_data = json.load(f)

        file_url_voice = f"/sing/voice/{json_data[song_data['filename']]}"
        file_url_bgm = f"/sing/bgm/{json_data[song_data['filename']]}"
    return {
        "music_name": display_name,
        "voice_url":'http://127.0.0.1:9000'+file_url_voice,
        "bgm_url":'http://127.0.0.1:9000'+file_url_bgm,
        "voice_type": song_data["ext"].lstrip('.').lower(),
        "bgm_type": song_data["ext"].lstrip('.').lower(),
        "type": "sing"
    }
# 启动服务器
if __name__ == "__main__":
    # 使用 streamable-http 传输方式
    mcp.run(transport="streamable-http")