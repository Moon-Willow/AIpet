# AI桌宠

AIPet是一款基于 Unity + Python 的智能桌面伴侣，支持语音对话、TTS、工具调用等功能。

## 主要特性

* LLM 对话 — 接入任何兼容 OpenAI API 的大模型（Ollama、DeepSeek 等），实现自然闲聊。
* Live2D 角色 — 桌宠形象采用Live2D 模型，可眨眼、口型同步。
* 分层记忆系统 — 短期上下文 + 近期摘要 + 长期记忆库。
* 语音输入与输出 — 支持离线语音识别和TTS(GPT-SoVITS)。
* 音乐播放与唱歌 — 模糊匹配本地音乐库，可播放歌曲或让桌宠唱歌给你听。

## 如何使用

请先拥有一个大模型的api\_key,下载Releases中的整合包，注意把4个zip文件全下载一个文件夹里，然后解压后缀为.zip的文件。按照其中的使用手册进行操作。

## 项目中使用的其他模型

* sherpa-onnx-sense-voice-small：https://www.modelscope.cn/models/xiaowangge/sherpa-onnx-sense-voice-small
* shibing624\_text2vec-base-chinese：https://www.modelscope.cn/models/zjwan461/shibing624\_text2vec-base-chinese
* bert-chinese-sentiment：https://www.modelscope.cn/models/google-bert/bert-base-chinese/summary

  * 该模型经过微调，微调数据集：https://www.modelscope.cn/datasets/zhangzhihao/Simplified\_Chinese\_Multi-Emotion\_Dialogue\_Dataset



