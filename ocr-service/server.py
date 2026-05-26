from flask import Flask, request, jsonify
from rapidocr_onnxruntime import RapidOCR
import tempfile
import os
import requests

app = Flask(__name__)
ocr = RapidOCR()

@app.route('/health', methods=['GET'])
def health():
    return 'ok', 200

@app.route('/ocr', methods=['POST'])
def run_ocr():
    tmp_path = None
    try:
        image_url = request.json.get('image_url') if request.is_json else None
        if not image_url:
            return jsonify({'error': 'No image_url provided'}), 400

        img_response = requests.get(image_url, timeout=10)
        with tempfile.NamedTemporaryFile(delete=False, suffix='.png') as f:
            f.write(img_response.content)
            tmp_path = f.name

        result, elapse = ocr(tmp_path)

        blocks = []
        if result:
            for item in result:
                box, text, score = item
                blocks.append({
                    'text': text,
                    'confidence': float(score),
                    'box': box
                })

        print(f"OCR done: {len(blocks)} blocks", flush=True)
        return jsonify({'data': blocks})

    except Exception as e:
        import traceback
        print("OCR ERROR:", traceback.format_exc(), flush=True)
        return jsonify({'error': str(e)}), 500
    finally:
        if tmp_path and os.path.exists(tmp_path):
            os.unlink(tmp_path)

if __name__ == '__main__':
    port = int(os.environ.get('PORT', 23333))
    app.run(host='0.0.0.0', port=port)