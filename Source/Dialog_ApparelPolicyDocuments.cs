using UnityEngine;
using Verse;

namespace ApparelPolicyBuilder
{
    public class Dialog_ApparelPolicyDocuments : Window
    {
        private const float RowHeight = 30f;

        private readonly Dialog_ApparelPolicyBuilder parent;
        private string nameBuffer;
        private Vector2 scroll;

        public override Vector2 InitialSize => new Vector2(420f, 460f);

        public Dialog_ApparelPolicyDocuments(Dialog_ApparelPolicyBuilder parent)
        {
            this.parent = parent;
            nameBuffer = parent.PolicyLabel;
            doCloseX = true;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            var titleRect = new Rect(inRect.x, inRect.y, inRect.width, 34f);
            using (new TextBlock(GameFont.Medium))
                Widgets.Label(titleRect, "APB.Documents".Translate());

            const float saveW = 90f, gap = 6f, fieldH = 30f;
            float y = titleRect.yMax + 4f;
            var nameRect = new Rect(inRect.x, y, inRect.width - saveW - gap, fieldH);
            nameBuffer = Widgets.TextField(nameRect, nameBuffer);
            string trimmed = nameBuffer?.Trim();
            bool canSave = !parent.WorkingIsEmpty && !trimmed.NullOrEmpty();

            var saveRect = new Rect(nameRect.xMax + gap, y, saveW, fieldH);
            TooltipHandler.TipRegionByKey(saveRect, "APB.SaveDocTip");
            if (Widgets.ButtonText(saveRect, "APB.SaveDoc".Translate(), active: canSave) && canSave)
                SaveCurrent(trimmed);

            y = saveRect.yMax + 8f;
            var listRect = new Rect(inRect.x, y, inRect.width, inRect.yMax - y);
            DrawList(listRect);
        }

        private void DrawList(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            var inner = rect.ContractedBy(4f);

            var docs = ApparelPolicyBuilderMod.Documents;
            if (docs.Count == 0)
            {
                Color prev = GUI.color;
                GUI.color = new Color(0.7f, 0.7f, 0.7f);
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(inner, "APB.NoDocuments".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = prev;
                return;
            }

            var viewRect = new Rect(0f, 0f, inner.width - 16f, docs.Count * RowHeight);
            Widgets.BeginScrollView(inner, ref scroll, viewRect);
            float ry = 0f;
            for (int i = 0; i < docs.Count; i++)
            {
                RuleDocument doc = docs[i];
                var rowRect = new Rect(0f, ry, viewRect.width, RowHeight);
                if (Mouse.IsOver(rowRect)) Widgets.DrawHighlight(rowRect);

                var trashRect = new Rect(rowRect.xMax - 26f, rowRect.y + (RowHeight - 22f) / 2f, 22f, 22f);
                if (Widgets.ButtonImage(trashRect, TexButton.Delete))
                    ConfirmDelete(doc);

                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(new Rect(rowRect.x + 6f, rowRect.y, rowRect.width - 34f, RowHeight), doc.name);
                Text.Anchor = TextAnchor.UpperLeft;

                if (Widgets.ButtonInvisible(new Rect(rowRect.x, rowRect.y, rowRect.width - 30f, RowHeight)))
                {
                    parent.LoadFromDocument(doc);
                    Close();
                }
                ry += RowHeight;
            }
            Widgets.EndScrollView();
        }

        private void SaveCurrent(string name)
        {
            Ruleset rs = parent.WorkingSnapshot();
            if (ApparelPolicyBuilderMod.FindDocument(name) != null)
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    "APB.OverwriteDocConfirm".Translate(name),
                    () => ApparelPolicyBuilderMod.SaveDocument(name, rs)));
            else
                ApparelPolicyBuilderMod.SaveDocument(name, rs);
        }

        private void ConfirmDelete(RuleDocument doc)
            => Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                "APB.DeleteDocConfirm".Translate(doc.name),
                () => ApparelPolicyBuilderMod.DeleteDocument(doc.name), destructive: true));
    }
}
