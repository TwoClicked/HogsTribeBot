from flask import Flask, request, jsonify
from paddleocr import PaddleOCR
import tempfile
import os
import requests

app = Flask(__name__)
ocr = None

def get_ocr():
    global ocr
    if ocr is None:
        ocr = PaddleOCR(use_textline_orientation=True, lang='en')
    return ocr

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

        result = get_ocr().predict(tmp_path)

        blocks = []
        for res in result:
            texts = res.get('rec_texts', [])
            boxes = res.get('rec_boxes', [])
            scores = res.get('rec_scores', [])
            for i, text in enumerate(texts):
                box = boxes[i].tolist() if i < len(boxes) else [[0, 0]]
                score = float(scores[i]) if i < len(scores) else 1.0
                blocks.append({'text': text, 'confidence': score, 'box': box})

        return jsonify({'data': blocks})

    except Exception as e:
        import traceback
        print("OCR ERROR:", traceback.format_exc())
        return jsonify({'error': str(e)}), 500
    finally:
        if tmp_path and os.path.exists(tmp_path):
            os.unlink(tmp_path)

if __name__ == '__main__':
    port = int(os.environ.get('PORT', 23333))
    app.run(host='0.0.0.0', port=port)