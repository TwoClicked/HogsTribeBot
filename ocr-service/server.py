from flask import Flask, request, jsonify
from paddleocr import PaddleOCR
import tempfile
import os
import requests

app = Flask(__name__)
ocr = PaddleOCR(use_textline_orientation=True, lang='en')

@app.route('/ocr', methods=['POST'])
def run_ocr():
    tmp_path = None
    try:
        image_url = request.json.get('image_url') if request.is_json else None

        if not image_url:
            return jsonify({'error': 'No image_url provided'}), 400

        response = requests.get(image_url)
        with tempfile.NamedTemporaryFile(delete=False, suffix='.png') as f:
            f.write(response.content)
            tmp_path = f.name

        result = ocr.predict(tmp_path)

        print("OCR RAW TYPE:", type(result))
        print("OCR RAW RESULT:", result)

        blocks = []
        for i, res in enumerate(result):
            print(f"ITEM {i} TYPE:", type(res))
            print(f"ITEM {i} KEYS:", res.keys() if hasattr(res, 'keys') else dir(res))
            print(f"ITEM {i}:", res)

        return jsonify({'data': blocks})

    except Exception as e:
        import traceback
        print("OCR ERROR:", str(e))
        print(traceback.format_exc())
        return jsonify({'error': str(e)}), 500
    finally:
        if tmp_path and os.path.exists(tmp_path):
            os.unlink(tmp_path)

if __name__ == '__main__':
    port = int(os.environ.get('PORT', 23333))
    app.run(host='0.0.0.0', port=port)