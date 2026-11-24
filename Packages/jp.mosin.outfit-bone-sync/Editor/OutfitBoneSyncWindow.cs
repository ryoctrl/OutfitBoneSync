using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace OutfitBoneSync.Editor
{
    /// <summary>
    /// アバターと衣装を選び、適用するとどのボーンがどう変わるかを見てから、適用・元に戻すを行うウィンドウ。
    /// ボタンを押すまでシーンは変えない。
    /// </summary>
    public class OutfitBoneSyncWindow : EditorWindow
    {
        [SerializeField] Transform _avatar;
        [SerializeField] GameObject _reference;
        [SerializeField] bool _autoFollow = true;
        [SerializeField] List<Transform> _extra = new List<Transform>();
        [SerializeField] List<Transform> _selected = new List<Transform>();

        class Entry
        {
            public Transform root;
            public OutfitBoneSync comp;
            public SyncPlan plan;
        }

        readonly List<Entry> _entries = new List<Entry>();
        readonly List<Transform> _sceneAvatars = new List<Transform>();
        readonly HashSet<Transform> _collapsed = new HashSet<Transform>();
        List<AvatarBoneChange> _avatarChanges = new List<AvatarBoneChange>();
        string _referenceDescription = "";
        List<ReferenceMiss> _referenceMisses = new List<ReferenceMiss>();
        bool _showReferenceMisses;
        double _lastRefresh = -10;
        bool _dirty = true;
        bool _showAvatarChanges = true;
        Vector2 _scroll;
        string _message;

        public static OutfitBoneSyncWindow Open()
        {
            var w = GetWindow<OutfitBoneSyncWindow>();
            w.minSize = new Vector2(600, 460);
            w.Show();
            return w;
        }

        /// <summary>Hierarchy で選んでいる物から、アバター（と衣装）を入れて開く。</summary>
        public static OutfitBoneSyncWindow OpenForSelection()
        {
            var w = Open();
            var go = Selection.activeGameObject;
            if (go != null && !EditorUtility.IsPersistent(go)) w.SetFromSelection(go.transform);
            return w;
        }

        /// <summary>
        /// アバターを選んでいればそのアバターと、その下の衣装すべて。
        /// 衣装（またはその中の物）を選んでいれば、その衣装のアバターと、その衣装だけを選ぶ。
        /// </summary>
        public void SetFromSelection(Transform t)
        {
            if (t.GetComponent<VRC.SDKBase.VRC_AvatarDescriptor>() != null)
            {
                SetAvatar(t);
                return;
            }
            var avatar = BoneSyncCore.FindAvatarRoot(t);
            if (avatar == null) return;
            var outfit = t;
            while (outfit.parent != null && outfit.parent != avatar) outfit = outfit.parent;
            SetAvatarAndOutfit(avatar, BoneSyncCore.FindOutfitCandidates(avatar).Contains(outfit) ? outfit : null);
        }

        public void SetAvatarAndOutfit(Transform avatar, Transform outfit)
        {
            SetAvatar(avatar);
            if (outfit == null) return;
            _selected.Clear();
            _selected.Add(outfit);
            if (!BoneSyncCore.FindOutfitCandidates(avatar).Contains(outfit)) _extra.Add(outfit);
            _dirty = true;
        }

        public void SetAvatar(Transform avatar)
        {
            if (_avatar == avatar) return;
            _avatar = avatar;
            _extra.Clear();
            _selected = BoneSyncCore.FindOutfitCandidates(avatar);
            var existing = _selected.Select(x => x.GetComponent<OutfitBoneSync>())
                .FirstOrDefault(x => x != null && x.referenceAvatar != null);
            _reference = existing != null ? existing.referenceAvatar : null;
            _message = null;
            _dirty = true;
        }

        public void SetSelected(Transform outfit, bool selected)
        {
            _selected.Remove(outfit);
            if (selected) _selected.Add(outfit);
            _dirty = true;
        }

        void OnEnable()
        {
            // 開いたままのウィンドウも、再読み込みのたびに今のタイトルにする
            titleContent = new GUIContent("Outfit Bone Sync");
            Undo.undoRedoPerformed += MarkDirty;
            EditorApplication.hierarchyChanged += MarkDirty;
        }

        void OnDisable()
        {
            Undo.undoRedoPerformed -= MarkDirty;
            EditorApplication.hierarchyChanged -= MarkDirty;
        }

        void MarkDirty()
        {
            _dirty = true;
            Repaint();
        }

        // アバターのボーンを Inspector で変えたときも表を新しくする
        void OnInspectorUpdate() => Repaint();

        bool IsSelected(Entry e) => _selected.Contains(e.root);

        void Refresh()
        {
            _entries.Clear();
            _avatarChanges = new List<AvatarBoneChange>();
            _referenceDescription = "";
            _referenceMisses = new List<ReferenceMiss>();
            _sceneAvatars.Clear();
            _sceneAvatars.AddRange(FindObjectsByType<VRC.SDKBase.VRC_AvatarDescriptor>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(x => !EditorUtility.IsPersistent(x))
                .Select(x => x.transform)
                .OrderBy(x => x.gameObject.scene.name).ThenBy(x => x.GetSiblingIndex()));
            if (_avatar == null) return;

            var outfits = BoneSyncCore.FindOutfitCandidates(_avatar);
            _extra.RemoveAll(x => x == null);
            foreach (var e in _extra)
                if (!outfits.Contains(e)) outfits.Add(e);
            _selected.RemoveAll(x => x == null);

            var matched = new HashSet<Transform>();
            foreach (var o in outfits)
            {
                var comp = o.GetComponent<OutfitBoneSync>();
                var entry = new Entry
                {
                    root = o,
                    comp = comp,
                    plan = BoneSyncCore.Plan(o, _avatar, _reference, comp != null ? comp.records : null),
                };
                _entries.Add(entry);
                if (IsSelected(entry))
                    foreach (var bp in entry.plan.bones)
                        if (bp.avatar != null) matched.Add(bp.avatar);
            }
            _avatarChanges = BoneSyncCore.ListAvatarChanges(_avatar, _reference, matched);
            _referenceDescription = new BoneSyncCore.ReferencePose(_avatar, _reference).Description;
            _referenceMisses = BoneSyncCore.ListReferenceMisses(_avatar, _reference);
        }

        void OnGUI()
        {
            if (Event.current.type == EventType.Layout &&
                (_dirty || EditorApplication.timeSinceStartup - _lastRefresh > 0.5))
            {
                Refresh();
                _dirty = false;
                _lastRefresh = EditorApplication.timeSinceStartup;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            DrawAvatar();
            if (_avatar != null)
            {
                DrawOutfits();
                DrawPreview();
            }
            EditorGUILayout.EndScrollView();
            if (_avatar != null) DrawFooter();
        }

        static void Header(string text)
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField(text, EditorStyles.boldLabel);
        }

        void ShowAvatarMenu(Rect rect)
        {
            var menu = new GenericMenu();
            foreach (var a in _sceneAvatars)
            {
                var avatar = a;
                menu.AddItem(new GUIContent($"{avatar.name}（{avatar.gameObject.scene.name}）"), avatar == _avatar, () =>
                {
                    SetAvatar(avatar);
                    Repaint();
                });
            }
            if (_sceneAvatars.Count == 0) menu.AddDisabledItem(new GUIContent("シーン内にアバターなし"));
            menu.DropDown(rect);
        }

        void DrawAvatar()
        {
            Header("アバター");
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    var dropped = (GameObject)EditorGUILayout.ObjectField(
                        new GUIContent("アバター", "ドラッグ: アバター、またはその中のオブジェクト（衣装・ボーンなど）"),
                        _avatar != null ? _avatar.gameObject : null, typeof(GameObject), true);
                    if (dropped != null && (_avatar == null || dropped != _avatar.gameObject) && !EditorUtility.IsPersistent(dropped))
                    {
                        var root = dropped.GetComponent<VRC.SDKBase.VRC_AvatarDescriptor>() != null
                            ? dropped.transform
                            : BoneSyncCore.FindAvatarRoot(dropped.transform);
                        if (root != null) SetAvatar(root);
                    }
                    if (EditorGUILayout.DropdownButton(new GUIContent("シーンから選択"), FocusType.Keyboard, GUILayout.Width(110)))
                        ShowAvatarMenu(GUILayoutUtility.GetLastRect());
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("選択中のオブジェクトから設定"))
                    {
                        var go = Selection.activeGameObject;
                        if (go != null && !EditorUtility.IsPersistent(go)) SetFromSelection(go.transform);
                    }
                    using (new EditorGUI.DisabledScope(_avatar == null))
                        if (GUILayout.Button("Hierarchy で選択")) Reveal(_avatar);
                }

                if (_avatar == null)
                {
                    EditorGUILayout.HelpBox("アバター未指定", MessageType.Info);
                    return;
                }

                EditorGUILayout.LabelField("シーン", _avatar.gameObject.scene.name);

                EditorGUI.BeginChangeCheck();
                _reference = (GameObject)EditorGUILayout.ObjectField(
                    new GUIContent("基準アバター（任意）", "改変前のアバター。未指定時は元 FBX のボーン値を基準値として使用"),
                    _reference, typeof(GameObject), true);
                if (EditorGUI.EndChangeCheck()) _dirty = true;
                EditorGUILayout.LabelField("基準値の取得元", _referenceDescription);
                BoneSyncGUI.DrawReferenceMisses(_avatar, _referenceMisses, ref _showReferenceMisses);

                _showAvatarChanges = EditorGUILayout.Foldout(_showAvatarChanges,
                    $"変更済みボーン（{_avatarChanges.Count}）", true);
                if (_showAvatarChanges)
                {
                    if (_avatarChanges.Count == 0)
                        EditorGUILayout.LabelField("なし（ルートの Scale は対象外）",
                            EditorStyles.wordWrappedMiniLabel);
                    BoneSyncGUI.DrawAvatarChanges(_avatar, _avatarChanges);
                }
            }
        }

        void DrawOutfits()
        {
            Header("対象衣装");
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (_entries.Count == 0)
                    EditorGUILayout.HelpBox("アバター直下に衣装なし。「衣装を追加」で指定。", MessageType.Info);
                foreach (var e in _entries)
                {
                    using (var row = new EditorGUILayout.HorizontalScope())
                    {
                        // Hierarchy で選択中の衣装の行は背景を付ける（Hierarchy 側で選んだときも同じ）
                        if (Event.current.type == EventType.Repaint && Selection.transforms.Contains(e.root))
                            EditorGUI.DrawRect(row.rect, SelectedRowColor);
                        bool sel = IsSelected(e);
                        bool now = EditorGUILayout.Toggle(sel, GUILayout.Width(18));
                        if (now != sel) SetSelected(e.root, now);
                        BoneSyncGUI.ObjectLabel(e.root, GUILayout.Width(220));
                        GUILayout.Label(Status(e), EditorStyles.miniLabel);
                    }
                }
                var add = (GameObject)EditorGUILayout.ObjectField("衣装を追加", null, typeof(GameObject), true);
                if (add != null && !EditorUtility.IsPersistent(add) && !_entries.Any(x => x.root == add.transform))
                {
                    _extra.Add(add.transform);
                    _selected.Add(add.transform);
                    _dirty = true;
                }
            }
        }

        static readonly Color SelectedRowColor = new Color(0.24f, 0.48f, 0.9f, 0.3f);

        /// <summary>Hierarchy で選択し、折りたたまれていれば開いて点滅させる。</summary>
        static void Reveal(Transform t)
        {
            if (t == null) return;
            Selection.activeTransform = t;
            EditorGUIUtility.PingObject(t);
        }

        static string Status(Entry e)
        {
            if (e.plan.pairCount == 0) return "対応ボーンなし";
            int n = e.plan.ChangeCount;
            if (e.comp == null) return n > 0 ? $"未適用 / 変更 {n}" : "未適用 / 変更なし";
            var s = e.comp.autoFollow ? "適用済み / 自動追従" : "適用済み";
            return n > 0 ? $"{s} / 未反映 {n}" : s;
        }

        void DrawPreview()
        {
            Header("変更内容");
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(
                    "対象: 衣装ボーンの Transform（Position / Rotation / Scale）。メッシュ・ウェイト・マテリアルは変更しない。" +
                    "適用前: 適用済み衣装に記録された元の値。",
                    EditorStyles.wordWrappedMiniLabel);
                var selected = _entries.Where(IsSelected).ToList();
                if (selected.Count == 0)
                    EditorGUILayout.HelpBox("対象衣装なし", MessageType.Info);
                foreach (var e in selected)
                {
                    EditorGUILayout.Space(4);
                    int n = e.plan.ChangeCount;
                    int applied = e.plan.bones.Count(x => x.DiffersFromOriginal);
                    string title = n > 0 ? $"変更 {n}"
                        : e.comp != null ? $"適用済み / 変更済み {applied}"
                        : "変更なし";
                    bool open = !_collapsed.Contains(e.root);
                    bool nowOpen;
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        nowOpen = GUILayout.Toggle(open, GUIContent.none, EditorStyles.foldout, GUILayout.Width(14));
                        BoneSyncGUI.ObjectLabel(e.root, GUILayout.Width(220));
                        GUILayout.Label(title, EditorStyles.boldLabel);
                    }
                    if (nowOpen != open)
                    {
                        if (nowOpen) _collapsed.Remove(e.root);
                        else _collapsed.Add(e.root);
                    }
                    if (!nowOpen) continue;
                    foreach (var w in e.plan.warnings) EditorGUILayout.HelpBox(w, MessageType.Warning);
                    using (new EditorGUI.IndentLevelScope())
                        BoneSyncGUI.DrawPlanTable(e.plan);
                }
            }
        }

        void DrawFooter()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                _autoFollow = EditorGUILayout.ToggleLeft(new GUIContent("自動追従", "アバターのボーン変更時に再適用"), _autoFollow);
                var selected = _entries.Where(IsSelected).ToList();
                int canApply = selected.Count(x => x.plan.pairCount > 0);
                int canRevert = selected.Count(x => x.comp != null);
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(canApply == 0))
                        if (GUILayout.Button($"適用（{canApply}）", GUILayout.Height(30)))
                        {
                            ApplySelected();
                            GUIUtility.ExitGUI();
                        }
                    using (new EditorGUI.DisabledScope(canRevert == 0))
                        if (GUILayout.Button($"元に戻す（{canRevert}）", GUILayout.Height(30)))
                        {
                            RevertSelected();
                            GUIUtility.ExitGUI();
                        }
                }
                if (!string.IsNullOrEmpty(_message))
                    EditorGUILayout.HelpBox(_message, MessageType.Info);
            }
        }

        /// <summary>チェックした衣装に適用する（衣装ごとに 1 回の Ctrl+Z で取り消せる）。</summary>
        public List<string> ApplySelected()
        {
            Refresh();
            var messages = new List<string>();
            foreach (var e in _entries.Where(IsSelected).ToList())
            {
                if (e.plan.pairCount == 0)
                {
                    messages.Add($"{e.root.name}: 対応ボーンなし（スキップ）");
                    continue;
                }
                Undo.IncrementCurrentGroup();
                var group = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("Outfit Bone Sync 適用");
                var c = e.comp != null ? e.comp : Undo.AddComponent<OutfitBoneSync>(e.root.gameObject);
                Undo.RecordObject(c, "Outfit Bone Sync 適用");
                c.avatarRoot = BoneSyncCore.FindAvatarRoot(e.root) == _avatar ? null : _avatar;
                c.referenceAvatar = _reference;
                c.autoFollow = _autoFollow;
                EditorUtility.SetDirty(c);
                if (PrefabUtility.IsPartOfPrefabInstance(c)) PrefabUtility.RecordPrefabInstancePropertyModifications(c);
                var r = BoneSyncCore.Apply(c, true);
                OutfitBoneSyncAutoFollow.Forget(c);
                Undo.CollapseUndoOperations(group);
                messages.Add($"{e.root.name}: 適用（変更 {r.changedCount}）");
            }
            _message = messages.Count > 0 ? string.Join("\n", messages) + "\n取り消し: 元に戻す / Ctrl+Z" : null;
            _dirty = true;
            Repaint();
            return messages;
        }

        /// <summary>チェックした適用済みの衣装を、合わせる前の値に戻してコンポーネントを外す。</summary>
        public List<string> RevertSelected()
        {
            Refresh();
            var messages = new List<string>();
            foreach (var e in _entries.Where(x => IsSelected(x) && x.comp != null).ToList())
            {
                var name = e.root.name;
                var n = BoneSyncCore.RevertAndRemove(e.comp);
                messages.Add($"{name}: 元に戻す（復元 {n}、コンポーネント削除）");
            }
            _message = messages.Count > 0 ? string.Join("\n", messages) + "\n取り消し: Ctrl+Z" : null;
            _dirty = true;
            Repaint();
            return messages;
        }
    }
}
