# Hyperpigmentation annotation rule, version 2 (written 2026-10-12)

**Why:** the blind re-box of 30 validation photos showed the first rule is not reproducible. The same annotator agreed with the earlier boxes at F1 0.28 (IoU 0.5).
The two passes had 3.0 vs 6.4 boxes per photo, and 140 of the 193 new boxes had no match in the original (median size 0.09 vs 0.125 for the original boxes).
The old rule ("one box per patch") did not say what a patch is, how far apart two patches must be to count as two, or how small a mark may be.

## The rule (apply exactly, in this order)
1. **Look at the whole face at normal size first.** Box only what you notice at that size as an area of darker skin than its surroundings (melasma, post-inflammatory marks, sun spots, dark patches). Do not zoom in to find more.
2. **One box = one connected patch.** If two dark areas touch, or the gap between them is smaller than the smaller one's width, they are ONE patch: draw one box around both.
3. **Separate patches stay separate** only if the gap is at least as wide as the smaller patch and normal skin is clearly visible between them.
4. **Minimum size:** do not box a mark whose box would be smaller than about 7 % of the photo's short side (about the size of a fingertip-sized mark on a face photo). Very small dots are skipped, not boxed.
5. **Speckled areas (many tiny dots, freckles):** if the dots are all below the minimum size, draw ONE box around the speckled area if it is clearly darker as a whole. Otherwise skip it.
6. **A patch stays inside one facial region:** forehead, cheek (left or right), nose, upper lip, chin, temple. If a dark area continues into the next region, end the box at the region border and start a new one there only if the part in the next region is also at least the minimum size.
7. **Box edge:** place the edge where the darkening has faded to about half. Do not include the soft fading rim and do not cut into the dark core.
8. **Count check:** typical photos have 1 to 5 boxes. If you reach 8, stop and merge touching neighbours (rule 2).
9. **No patch you would call a mole, scar, shadow, hair, beard or lip colour.** If nothing qualifies, press X.
10. **Do not look at older boxes or at model drafts** while boxing.

## Short text shown inside the box tool
One box per CONNECTED dark patch (touching or closer than the smaller one's width = one box). Skip marks smaller than 7 % of the photo. Speckles = one box if the area is darker as a whole. Stay inside one facial region. Edge at half fade. Usually 1-5 boxes. No zooming to find more. X = nothing qualifies.

## How the rule is tested
- **Pass A and pass B** on the same 30 validation photos, at least 12 hours apart, in a different order, without looking at the old boxes.
- Agreement of A with B is the reliability under rule v2. For comparison, version 1 gave F1 0.28 at IoU 0.5.
- Decision: **0.50 or higher** at IoU 0.5 means the rule is reproducible and relabeling the training photos is worth it. **Below 0.40** means the box-level task is too ambiguous and the thesis states the ceiling and reports zone-level and photo-level results as the main claim. In between, adjust the rule once and test again.
- The original labels of validation, test and independent sets are never changed.
