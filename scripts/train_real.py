from ultralytics import YOLO

if __name__ == '__main__':

    # ── Train ────────────────────────────────────────────────────────────────
    model = YOLO('ul://ultralytics/yolo26/yolo26n')

    results = model.train(
        data='ul://omar-hafez/datasets/venom-v71',

        # --- Optimized Training Fundamentals ---
        epochs=100,
        patience=15,
        imgsz=640,

        # --- Hardware Optimizations ---
        device='7',             # Forces training on your dedicated GPU
        batch=-1,               # Auto-batching to maximize VRAM
        amp=True,               # Automatic Mixed Precision

        # --- Project & Custom Configurations ---
        project='omar-hafez/v71venom',
        name='v7y26n',
        angle=1.0,
        rle=1.0
    )

    # ── Evaluate on unseen test split ────────────────────────────────────────
    best = YOLO('omar-hafez/v71venom/v7y26n/weights/best.pt')

    print("Starting final evaluation on unseen test data...")
    metrics = best.val(split='test')

    print(f"Final Test mAP50:    {metrics.box.map50:.3f}")
    print(f"Final Test mAP50-95: {metrics.box.map:.3f}")

    # ── Export Model ─────────────────────────────────────────────────────────
    print("\nExporting to TorchScript...")
    success = best.export(
        format='torchscript',   
        simplify=True,          
        imgsz=640
    )

    print(f"Exported successfully: {success}")
