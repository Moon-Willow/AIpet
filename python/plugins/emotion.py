import torch
from transformers import AutoTokenizer, AutoModelForSequenceClassification
from .base import BasePlugin

class EmotionPlugin(BasePlugin):
    def __init__(self):
        super().__init__()
        self.model = None
        self.tokenizer = None
        self.emotion_list = ['开心', '关心', '平静', '生气', '伤心', '疑问', '厌恶', '惊讶']

    def configure(self, global_config: dict):
        super().configure(global_config)
        # 仅当启用时才加载模型（节省资源）
        if self.enabled:
            self._load_model()

    def _load_model(self):
        try:
            model_path = "./bert-chinese-sentiment"
            self.tokenizer = AutoTokenizer.from_pretrained(model_path)
            self.model = AutoModelForSequenceClassification.from_pretrained(model_path)
            self.model.eval()
            print("情感模型加载成功")
        except Exception as e:
            print(f"情感模型加载失败: {e}")

    def predict(self, text: str):
        if not self.model or not self.tokenizer:
            return "平静", 0.0
        inputs = self.tokenizer(text, padding="max_length", truncation=True,
                                max_length=128, return_tensors="pt")
        with torch.no_grad():
            outputs = self.model(**inputs)
            probs = torch.nn.functional.softmax(outputs.logits, dim=-1)
            pred_idx = torch.argmax(probs, dim=-1).item()
            confidence = probs[0][pred_idx].item()
            pred_label = self.model.config.id2label[pred_idx]
        return pred_label, confidence

    def on_assistant_output(self, text: str, global_config: dict) -> dict:
        if not self.enabled:
            return {}
        threshold = self.config.get("threshold", 0.6)
        mapping = self.config.get("mapping", {})
        label, conf = self.predict(text)
        if conf < threshold:
            label = "平静"
        if label not in self.emotion_list:
            label = "平静"
        emotion_obj = mapping.get(label, {})
        return {"emotion": emotion_obj}