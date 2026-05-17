import os
import json
import base64
from PIL import Image
from pathlib import Path

def yolo_to_labelstudio(images_dir, labels_dir, output_json="labelstudio_tasks.json"):
    """
    Convert YOLO format annotations to Label Studio JSON format
    
    Args:
        images_dir: Path to images folder
        labels_dir: Path to labels folder
        output_json: Output JSON file name
    """
    
    tasks = []
    
    # Get all image files
    image_files = list(Path(images_dir).glob("*.png")) + \
                  list(Path(images_dir).glob("*.jpg")) + \
                  list(Path(images_dir).glob("*.jpeg"))
    
    for image_path in image_files:
        # Create corresponding label file name
        label_filename = f"{image_path.stem}.txt"
        label_path = Path(labels_dir) / label_filename
        
        # Get image dimensions
        with Image.open(image_path) as img:
            img_width, img_height = img.size
        
        # Prepare Label Studio task structure
        task = {
            "data": {
                "image": f"/data/local-files/?d=images/{image_path.name}"
            },
            "predictions": [{
                "model_version": "yolo_converter",
                "score": 1.0,
                "result": []
            }]
        }
        
        # If label file exists, parse it
        if label_path.exists():
            with open(label_path, 'r') as f:
                lines = f.readlines()
            
            for line in lines:
                line = line.strip()
                if not line:
                    continue
                    
                parts = line.split()
                if len(parts) >= 5:
                    class_id = int(parts[0])
                    x_center = float(parts[1])
                    y_center = float(parts[2])
                    width = float(parts[3])
                    height = float(parts[4])
                    
                    # Convert YOLO format (normalized) to pixel coordinates
                    x_px = x_center * img_width
                    y_px = y_center * img_height
                    w_px = width * img_width
                    h_px = height * img_height
                    
                    # Calculate Label Studio coordinates (top-left x, top-left y, width, height)
                    x1 = x_px - (w_px / 2)
                    y1 = y_px - (h_px / 2)
                    
                    # Add to Label Studio format (percentages relative to image)
                    bbox_result = {
                        "id": f"bbox_{len(task['predictions'][0]['result'])}",
                        "type": "rectanglelabels",
                        "value": {
                            "x": (x1 / img_width) * 100,  # percentage
                            "y": (y1 / img_height) * 100, # percentage
                            "width": (w_px / img_width) * 100,   # percentage
                            "height": (h_px / img_height) * 100, # percentage
                            "rectanglelabels": [f"class_{class_id}"]
                        },
                        "origin": "manual",
                        "to_name": "image",
                        "from_name": "label"
                    }
                    
                    task["predictions"][0]["result"].append(bbox_result)
        
        tasks.append(task)
    
    # Save to JSON file
    with open(output_json, 'w') as f:
        json.dump(tasks, f, indent=2)
    
    print(f"Created {len(tasks)} tasks in {output_json}")
    return tasks

# Usage
if __name__ == "__main__":
    images_dir = "../../dataset_unity/images/train"  # Update with your path
    labels_dir = "../../dataset_unity/labels/train"  # Update with your path
    
    tasks = yolo_to_labelstudio(images_dir, labels_dir)