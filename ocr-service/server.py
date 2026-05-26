from flask import Flask, request, jsonify
import tempfile
import os
import requests
import traceback

app = Flask(__name__)
ocr = None

@app.route('/health', methods=['GET'])
def health():
    return 'ok', 200

@app.route('/ocr', methods=['POST'])
def run_ocr():
    global ocr
    tmp_path = None
    try:
        if ocr is None:
            print("Initializing PaddleOCR...", flush=True)
            from paddleocr import PaddleOCR
            ocr = PaddleOCR(use_textline_orientation=True, lang='en')
            print("PaddleOCR ready.", flush=True)

        image_url = request.json.get('image_url') if request.is_json else None
        if not image_url:
            return jsonify({'error': 'No image_url provided'}), 400

        img_response = requests.get(image_url, timeout=10)
        with tempfile.NamedTemporaryFile(delete=False, suffix='.png') as f:
            f.write(img_response.content)
            tmp_path = f.name

        print(f"Running OCR on {tmp_path}", flush=True)
        result = ocr.predict(tmp_path)
        print(f"OCR done, result type: {type(result)}", flush=True)
        print(f"OCR result: {result}", flush=True)

        blocks = []
        for res in result:
            print(f"RES: {res}", flush=True)
            texts = res.get('rec_texts', [])
            boxes = res.get('rec_boxes', [])
            scores = res.get('rec_scores', [])
            for i, text in enumerate(texts):
                box = boxes[i].tolist() if i < len(boxes) else [[0, 0]]
                score = float(scores[i]) if i < len(scores) else 1.0
                blocks.append({'text': text, 'confidence': score, 'box': box})

        print(f"Returning {len(blocks)} blocks", flush=True)
        return jsonify({'data': blocks})

    except Exception as e:
        print("OCR ERROR:", traceback.format_exc(), flush=True)
        return jsonify({'error': str(e)}), 500
    finally:
        if tmp_path and os.path.exists(tmp_path):
            os.unlink(tmp_path)

if __name__ == '__main__':
    port = int(os.environ.get('PORT', 23333))
    app.run(host='0.0.0.0', port=port)