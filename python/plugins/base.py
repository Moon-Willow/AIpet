class BasePlugin:
    def __init__(self):
        self.enabled = True
        self.config = {}

    def configure(self, global_config: dict):
        name = self.__class__.__name__
        self.config = global_config.get("plugins", {}).get(name, {})
        self.enabled = self.config.get("enabled", True)

    # ---------- 用户输入阶段 ----------
    def on_user_input(self, text: str) -> str:
        """用户消息发送前，可修改输入文本。返回修改后的文本"""
        return text

    def on_user_input_after(self, text: str) -> dict:
        """用户消息已接收并可做额外处理（如打日志），返回附加字段或不返回"""
        return {}

    # ---------- AI 回复阶段 ----------
    def on_assistant_output(self, text: str, global_config: dict) -> dict:
        """AI 回复生成后，可附加字段到响应（如情感、语音URL）"""
        return {}

    def on_assistant_output_before(self, text: str) -> str:
        """AI 回复准备返回前，可修改回复文本。返回修改后的文本"""
        return text