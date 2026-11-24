using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace OutfitBoneSync.Editor
{
    /// <summary>ウィンドウと Inspector で共通の表示（変わるボーンの表）。</summary>
    public static class BoneSyncGUI
    {
        public class Row
        {
            public Transform bone;
            public string item;
            public string original, current, next;
            /// <summary>適用すると今の値から変わる行。</summary>
            public bool changes;
        }

        /// <summary>合わせる前・今・合わせたあとのどれかが違う値を、ボーン・項目ごとの行にする。</summary>
        public static List<Row> Rows(SyncPlan plan)
        {
            var rows = new List<Row>();
            foreach (var bp in plan.bones)
            {
                if (bp.origPosition != bp.curPosition || bp.curPosition != bp.newPosition)
                    rows.Add(new Row
                    {
                        bone = bp.bone, item = "Position", changes = bp.curPosition != bp.newPosition,
                        original = Fmt(bp.origPosition), current = Fmt(bp.curPosition), next = Fmt(bp.newPosition),
                    });
                if (bp.origRotation != bp.curRotation || bp.curRotation != bp.newRotation)
                    rows.Add(new Row
                    {
                        bone = bp.bone, item = "Rotation", changes = bp.curRotation != bp.newRotation,
                        original = Fmt(bp.origRotation), current = Fmt(bp.curRotation), next = Fmt(bp.newRotation),
                    });
                if (bp.origScale != bp.curScale || bp.curScale != bp.newScale)
                    rows.Add(new Row
                    {
                        bone = bp.bone, item = "Scale", changes = bp.curScale != bp.newScale,
                        original = Fmt(bp.origScale), current = Fmt(bp.curScale), next = Fmt(bp.newScale),
                    });
            }
            return rows;
        }

        // ---- Hierarchy と同じ入れ子の表示 ----

        /// <summary>入れ子の表示の 1 行分。表示したいボーンと、そこまでの親（値の変わらないボーン）。</summary>
        public class TreeNode
        {
            public Transform bone;
            /// <summary>表示したいボーン（false なら、子をたどるために出す親）。</summary>
            public bool hasItem;
            public readonly List<TreeNode> children = new List<TreeNode>();
        }

        /// <summary>
        /// items のボーンを、root から下の Hierarchy の親子関係どおりの木にする。間の親も入れ、兄弟は Hierarchy の順に並べる。
        /// </summary>
        public static List<TreeNode> BuildTree(Transform root, IEnumerable<Transform> items)
        {
            var nodes = new Dictionary<Transform, TreeNode>();
            var tops = new List<TreeNode>();
            foreach (var item in items)
            {
                if (item == null) continue;
                TreeNode child = null;
                for (var t = item; t != null && t != root; t = t.parent)
                {
                    bool existed = nodes.TryGetValue(t, out var node);
                    if (!existed)
                    {
                        node = new TreeNode { bone = t };
                        nodes[t] = node;
                    }
                    if (t == item) node.hasItem = true;
                    if (child != null && !node.children.Contains(child)) node.children.Add(child);
                    if (existed) break;
                    if (t.parent == root || t.parent == null) tops.Add(node);
                    child = node;
                }
            }
            Sort(tops);
            return tops;
        }

        static void Sort(List<TreeNode> list)
        {
            list.Sort((a, b) => a.bone.GetSiblingIndex().CompareTo(b.bone.GetSiblingIndex()));
            foreach (var n in list) Sort(n.children);
        }

        // 折りたたんだボーン（ウィンドウと Inspector で共通）
        static readonly HashSet<int> Collapsed = new HashSet<int>();

        const float NameWidth = 250;
        const float IndentWidth = 12;

        /// <summary>木を上から順に、深さ付きでたどる（折りたたんだボーンの子は飛ばす）。</summary>
        static IEnumerable<KeyValuePair<TreeNode, int>> Walk(List<TreeNode> nodes, int depth = 0)
        {
            foreach (var n in nodes)
            {
                yield return new KeyValuePair<TreeNode, int>(n, depth);
                if (Collapsed.Contains(n.bone.GetInstanceID())) continue;
                foreach (var x in Walk(n.children, depth + 1)) yield return x;
            }
        }

        /// <summary>字下げ・折りたたみ・ボーン名（クリックで Hierarchy に表示）の欄。</summary>
        static void DrawNameCell(TreeNode node, int depth)
        {
            using (new EditorGUILayout.HorizontalScope(GUILayout.Width(NameWidth)))
            {
                GUILayout.Space(depth * IndentWidth);
                if (node.children.Count > 0)
                {
                    int id = node.bone.GetInstanceID();
                    bool expanded = !Collapsed.Contains(id);
                    if (GUILayout.Toggle(expanded, GUIContent.none, EditorStyles.foldout, GUILayout.Width(IndentWidth)) != expanded)
                    {
                        if (expanded) Collapsed.Add(id);
                        else Collapsed.Remove(id);
                        // 行の数が変わるので、このイベントの残りは描かずに描き直す
                        GUIUtility.ExitGUI();
                    }
                }
                else GUILayout.Space(IndentWidth);
                ObjectLabel(node.bone);
            }
        }

        /// <summary>
        /// GameObject の欄（読み取り専用）。クリックで Hierarchy 上の物をハイライトする（Unity 標準の動作）。
        /// 欄の値は変えられない。
        /// </summary>
        public static void ObjectLabel(Transform t, params GUILayoutOption[] options)
        {
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.ObjectField(t != null ? t.gameObject : null, typeof(GameObject), true, options);
        }

        /// <summary>
        /// 変わるボーンの表を、衣装の Armature の階層どおりの入れ子で描く。
        /// 「適用前」の列は、控えてある値が今と違う（適用済みの）ときだけ出す。
        /// </summary>
        public static void DrawPlanTable(SyncPlan plan)
        {
            var rows = Rows(plan);
            if (rows.Count == 0) return;
            bool showOriginal = rows.Any(r => r.original != r.current);
            var byBone = new Dictionary<Transform, List<Row>>();
            foreach (var r in rows)
            {
                if (!byBone.TryGetValue(r.bone, out var list)) byBone[r.bone] = list = new List<Row>();
                list.Add(r);
            }

            var head = EditorStyles.miniBoldLabel;
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("ボーン", head, GUILayout.Width(NameWidth));
                GUILayout.Label("項目", head, GUILayout.Width(62));
                if (showOriginal) GUILayout.Label("適用前", head, GUILayout.MinWidth(90));
                GUILayout.Label("現在", head, GUILayout.MinWidth(90));
                GUILayout.Label("適用後", head, GUILayout.MinWidth(90));
            }
            foreach (var x in Walk(BuildTree(plan.outfitRoot, byBone.Keys)))
            {
                if (!byBone.TryGetValue(x.Key.bone, out var boneRows))
                {
                    using (new EditorGUILayout.HorizontalScope())
                        DrawNameCell(x.Key, x.Value);
                    continue;
                }
                for (int i = 0; i < boneRows.Count; i++)
                {
                    var r = boneRows[i];
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (i == 0) DrawNameCell(x.Key, x.Value);
                        else GUILayout.Label("", GUILayout.Width(NameWidth));
                        GUILayout.Label(r.item, EditorStyles.miniLabel, GUILayout.Width(62));
                        if (showOriginal) GUILayout.Label(r.original, EditorStyles.miniLabel, GUILayout.MinWidth(90));
                        GUILayout.Label(r.current, EditorStyles.miniLabel, GUILayout.MinWidth(90));
                        GUILayout.Label(r.changes ? r.next : "—",
                            r.changes ? ChangedStyle : EditorStyles.miniLabel, GUILayout.MinWidth(90));
                    }
                }
            }
        }

        /// <summary>アバターの変更済みボーンを、アバターの Armature の階層どおりの入れ子で描く。</summary>
        public static void DrawAvatarChanges(Transform avatarRoot, List<AvatarBoneChange> changes)
        {
            var byBone = changes.ToDictionary(x => x.bone);
            foreach (var x in Walk(BuildTree(avatarRoot, changes.Select(c => c.bone))))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (byBone.TryGetValue(x.Key.bone, out var ch))
                        GUILayout.Label(new GUIContent(ch.outfitHasBone ? "○" : "－",
                                ch.outfitHasBone ? "衣装に該当ボーンあり" : "衣装に該当ボーンなし（追従対象外）"),
                            EditorStyles.miniLabel, GUILayout.Width(16));
                    else GUILayout.Label("", GUILayout.Width(16));
                    DrawNameCell(x.Key, x.Value);
                    if (ch != null) GUILayout.Label(Describe(ch), EditorStyles.miniLabel);
                }
            }
        }

        static GUIStyle _changedStyle;

        // 行の高さがずれないように、miniLabel と同じ大きさ・余白のまま太字にする
        static GUIStyle ChangedStyle => _changedStyle ??= new GUIStyle(EditorStyles.miniLabel) { fontStyle = FontStyle.Bold };

        public static string Fmt(Vector3 v) => $"({v.x:0.####}, {v.y:0.####}, {v.z:0.####})";

        /// <summary>Inspector と同じく、-180〜180 度のオイラー角で表す。</summary>
        public static string Fmt(Quaternion q)
        {
            var e = q.eulerAngles;
            return Fmt(new Vector3(Wrap(e.x), Wrap(e.y), Wrap(e.z)));
        }

        static float Wrap(float a)
        {
            a %= 360f;
            if (a > 180f) a -= 360f;
            if (a <= -180f) a += 360f;
            return Mathf.Abs(a) < 0.00005f ? 0f : a;
        }

        /// <summary>基準アバターに該当ボーンが無いときの警告と、ボーンごとの代わりの取得元の一覧。</summary>
        public static void DrawReferenceMisses(Transform avatarRoot, List<ReferenceMiss> misses, ref bool expanded)
        {
            if (misses == null || misses.Count == 0) return;
            EditorGUILayout.HelpBox(
                $"基準アバターに該当ボーンなし（{misses.Count}）。該当ボーンは別の取得元の値を使用。" +
                "基準アバターの階層（ルートからのパス）が対象アバターと異なる可能性あり",
                MessageType.Warning);
            expanded = EditorGUILayout.Foldout(expanded, $"該当ボーンなし（{misses.Count}）", true);
            if (!expanded) return;
            var byBone = misses.ToDictionary(x => x.bone);
            foreach (var x in Walk(BuildTree(avatarRoot, misses.Select(m => m.bone))))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    DrawNameCell(x.Key, x.Value);
                    if (byBone.TryGetValue(x.Key.bone, out var m)) GUILayout.Label(m.source, EditorStyles.miniLabel);
                }
            }
        }

        public static string Describe(AvatarBoneChange ch)
        {
            var parts = new List<string>();
            if (ch.positionDelta.sqrMagnitude > 1e-12f)
                parts.Add($"Position Δ{Fmt(ch.positionDelta)}");
            if (ch.rotationAngle > 0.001f)
                parts.Add($"Rotation {ch.rotationAngle:0.##}°");
            if ((ch.scaleRatio - Vector3.one).sqrMagnitude > 1e-10f)
                parts.Add($"Scale ×{Fmt(ch.scaleRatio)}");
            return string.Join("  ", parts);
        }
    }
}
