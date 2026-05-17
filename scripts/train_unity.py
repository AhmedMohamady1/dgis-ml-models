from ultralytics import YOLO


# Required for Windows multiprocessing
if __name__ == '__main__':

    # ── Train ────────────────────────────────────────────────────────────────
    model = YOLO('yolov26s.pt')  # small variant — heavier than nano, better accuracy

    results = model.train(
        data='configs/dataset_unity.yaml',

        # --- Optimized Training Fundamentals ---
        epochs=50,
        patience=10,
        imgsz=640,

        # --- Hardware Optimizations ---
        device=0,       # Forces training on your dedicated GPU
        workers=8,
        batch=-1,       # Auto-batching to maximize VRAM
        amp=True        # Automatic Mixed Precision
    )

    # ── Evaluate on unseen test split ────────────────────────────────────────
    best = YOLO('runs/detect/train/weights/best.pt')

    print("Starting final evaluation on unseen test data...")
    metrics = best.val(split='test')

    print(f"Final Test mAP50:    {metrics.box.map50:.3f}")
    print(f"Final Test mAP50-95: {metrics.box.map:.3f}")

    # ── Export to ONNX for Unity ──────────────────────────────────────────────
    print("\nExporting to ONNX...")
    success = best.export(
        format='onnx',
        imgsz=640,
        opset=12,
        simplify=True   # Simplifies the ONNX graph for better Unity compatibility
    )

    print(f"Exported successfully: {success}")
