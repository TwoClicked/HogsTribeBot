from flask import Flask, request, jsonify
from paddleocr import PaddleOCR
import tempfile
import os
import requests

app = Flask(__name__)
ocr = PaddleOCR(use_textline_orientation=True, lang='en')

@app.route('/ocr', methods=['POST'])
def run_ocr():
    try:
        image_url = request.json.get('image_url') if request.is_json else None

        if not image_url:
            return jsonify({'error': 'No image_url provided'}), 400

        response = requests.get(image_url)
        with tempfile.NamedTemporaryFile(delete=False, suffix='.png') as f:
            f.write(response.content)
            tmp_path = f.name

        result = ocr.predict(tmp_path)
        os.unlink(tmp_path)

        # DEBUG - remove after testing
        print("OCR RAW RESULT:", result)

        blocks = []
        for res in result:
            for item in res['rec_texts'] if 'rec_texts' in res else []:
                blocks.append({'text': item, 'confidence': 1.0, 'box': [[0,0]]})

        return jsonify({'data': blocks})

    except Exception as e:
        print("OCR ERROR:", str(e))
        return jsonify({'error': str(e)}), 500

if __name__ == '__main__':
    port = int(os.environ.get('PORT', 23333))
    app.run(host='0.0.0.0', port=port)