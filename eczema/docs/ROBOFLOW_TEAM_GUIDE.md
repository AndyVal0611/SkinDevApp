# Boxing hyperpigmentation photos together in Roboflow – team guide

Goal: box the 646 hyperpigmentation photos (one box per patch of darker skin) so the detector learns from consistent boxes.
Everyone works in the browser. No installs. The labels stay in one Roboflow project, and Elaiza exports them at the end.

Button names in Roboflow change from time to time. If a name below does not match what you see, look for the closest one.

---

## A. Elaiza: set up the project (once)

1. Make sure the notebook step is done: `PrecisionSkin_Hyperpigmentation_Boxing_Sprint.ipynb` → run cells 0, 1, 2, then **cell 6**.
   This creates the folder `C:\Users\Bautista\Downloads\hyperpigmentation_for_roboflow` (646 photos, about 23 MB).
2. In Roboflow: **Create New Project**
   - Project type: **Object Detection**
   - Annotation group / class: **hyperpigmentation** (spelled exactly like this, all lower case)
3. **Upload Data**: drag the whole folder `hyperpigmentation_for_roboflow` into the page and click Save and Continue.
   - Only photos from public datasets belong here. **Never upload participant captures** (our consent forms say images stay local).
   - On the free plan the project and its photos are public. Keep the original dataset credits in your references.
4. **Invite teammates**: project or workspace settings → Teammates / Invite. Add Francesca and Andrea by email.
   Do not share a password. (If the plan limits the number of seats, tell me and we use the local tool instead.)
5. **Split the work**: Annotate tab → assign the photos to the three of you in equal batches (about 215 photos each).

## B. Everyone: how to box

**The rule (agree on it together first, with 2–3 example photos):**

- Draw **one box around each patch of darker skin** (melasma, sun spots, post-acne dark marks), **edge to edge around the patch**.
- **Do not** box the whole face or a big region of the cheek. Separate patches get separate boxes.
- Boxes should be **tight**: no big margin around the patch.
- If you **cannot see any patch**, mark the photo as having **no annotation** (null / "mark as null"), do not draw anything.
- Same rule on every photo, also when you are tired. When unsure, do what you did on the previous similar photo.

**Tips**

- Use the bounding box tool (rectangle), not polygon.
- Zoom in on small patches; fix a box by dragging its corners.
- Work in sessions of 50–100 photos, then rest. Quality drops when tired.
- Do not change or delete the photos. Do not rename the class.

## C. Elaiza: when all photos are done

1. In Roboflow, check that every photo is either boxed or marked null (Annotate / Review tabs).
2. **Add Images to Dataset** (choose any train/valid/test split; the notebook uses all of them).
3. **Generate a version with NO preprocessing and NO augmentation** (leave resize, grayscale, flips, rotations, brightness etc. all off).
   Augmentation would create fake copies and change skin colour.
4. **Export** the version as **YOLOv8** and download the zip. Unzip it into `C:\Users\Bautista\Downloads\`.
5. Open the notebook **cell 7**, set `EXPORT_DIR` to the unzipped folder, and run it. It checks the class name, the number of boxed photos and the box sizes,
   and prints the line to paste into the detector notebook (`PrecisionSkin_Detector_V5_Format.ipynb`, cell 2, `OWN_BOX_SOURCES`).

## D. What happens to the old boxes

Some of these photos already exist in the public datasets with other boxes. In the detector notebook, `"replace_public_twins": True`
removes all public copies of every photo you re-boxed, so a photo never has two different sets of boxes. If a photo's public copy is in the
validation or test set, your photo is not used for training (so nothing leaks into the test set).

## E. Why consistency matters

The detector can only be as consistent as its labels. If one person boxes the whole cheek and another boxes each dark patch, the model learns a blurry idea
of "one patch". Agree on the rule first, and check each other's first 10 photos.
