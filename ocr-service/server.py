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
        # Accepts either a URL or raw image bytes
        image_url = request.json.get('image_url') if request.is_json else None

        if image_url:
            response = requests.get(image_url)
            suffix = '.png'
            with tempfile.NamedTemporaryFile(delete=False, suffix=suffix) as f:
                f.write(response.content)
                tmp_path = f.name
        else:
            return jsonify({'error': 'No image_url provided'}), 400

        result = ocr.ocr(tmp_path, cls=True)
        os.unlink(tmp_path)

        blocks = []
        if result and result[0]:
            for line in result[0]:
                box, (text, confidence) = line
                blocks.append({
                    'text': text,
                    'confidence': round(confidence, 4),
                    'box': box
                })

        return jsonify({'data': blocks})

    except Exception as e:
        return jsonify({'error': str(e)}), 500

if __name__ == '__main__':
    port = int(os.environ.get('PORT', 23333))
    app.run(host='0.0.0.0', port=port)