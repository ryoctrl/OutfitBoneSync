using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace OutfitBoneSync.Editor
{
    [CustomEditor(typeof(OutfitBoneSync))]
    public class OutfitBoneSyncInspector : UnityEditor.Editor
    {
        bool _showChanges = true;
        bool _showPlan = true;
        List<AvatarBoneChange> _changes = new List<AvatarBoneChange>();
        SyncPlan _plan;
        List<ReferenceMiss> _misses = new List<ReferenceMiss>();
        bool _showMisses;
        double _changesTime = -10;

        public override void OnInspectorGUI()
        {
            var c = (OutfitBoneSync)target;
            serializedObject.Update();

            EditorGUILayout.HelpBox(
                "アバターのボーン変更量（基準値との差）を衣装ボーンの Transform に適用。適用前の値は記録済み。" +
                "アバター・衣装の変更、適用前のプレビュー: ウィンドウ",
                MessageType.None);

            var root = BoneSyncCore.FindAvatarRoot(c);
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.ObjectField("対象アバター", root, typeof(Transform), true);

            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(OutfitBoneSync.avatarRoot)),
                new GUIContent("アバター（手動指定）", "未指定時は親の VRC Avatar Descriptor を使用"));
            // 基準アバターは通常不要なので出さない。旧版で設定済みのときだけ、外せるように出す
            if (c.referenceAvatar != null)
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(OutfitBoneSync.referenceAvatar)),
                    new GUIContent("基準アバター", "改変前のアバター。未指定時は元 FBX のボーン値を基準値として使用"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(OutfitBoneSync.autoFollow)),
                new GUIContent("自動追従", "アバターのボーン変更時に再適用"));
            if (serializedObject.ApplyModifiedProperties())
            {
                OutfitBoneSyncAutoFollow.Forget(c);
                _changesTime = -10;
            }

            if (root == null)
            {
                EditorGUILayout.HelpBox("対象アバターなし。衣装をアバターの子に配置、または「アバター（手動指定）」を設定。",
                    MessageType.Warning);
                return;
            }

            if (Event.current.type == EventType.Layout && EditorApplication.timeSinceStartup - _changesTime > 1.0)
            {
                _changes = BoneSyncCore.ListAvatarChanges(c);
                _plan = BoneSyncCore.Plan(c.transform, root, c.referenceAvatar, c.records);
                _misses = BoneSyncCore.ListReferenceMisses(root, c.referenceAvatar);
                _changesTime = EditorApplication.timeSinceStartup;
            }
            if (_plan == null) return;

            if (c.referenceAvatar != null)
            {
                EditorGUILayout.LabelField("基準値の取得元", _plan.referenceDescription);
                BoneSyncGUI.DrawReferenceMisses(root, _misses, ref _showMisses);
            }

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("ウィンドウを開く", GUILayout.Height(28)))
                {
                    OutfitBoneSyncWindow.Open().SetAvatarAndOutfit(root, c.transform);
                    GUIUtility.ExitGUI();
                }
                using (new EditorGUI.DisabledScope(_plan.ChangeCount == 0))
                {
                    if (GUILayout.Button(_plan.ChangeCount > 0 ? $"再適用（{_plan.ChangeCount}）" : "再適用",
                            GUILayout.Height(28)))
                    {
                        var r = BoneSyncCore.Apply(c, true);
                        OutfitBoneSyncAutoFollow.Forget(c);
                        OutfitBoneSyncMenu.Report(c, r);
                        _changesTime = -10;
                    }
                }
                if (GUILayout.Button(new GUIContent("元に戻す", "衣装ボーンを適用前の値に復元し、コンポーネントを削除。Ctrl+Z で取り消し可"),
                        GUILayout.Height(28)))
                {
                    var name = c.name;
                    var n = BoneSyncCore.RevertAndRemove(c);
                    Debug.Log($"[OutfitBoneSync] {name}: 元に戻す（復元 {n}、コンポーネント削除）");
                    GUIUtility.ExitGUI();
                }
            }

            EditorGUILayout.Space();
            int applied = _plan.bones.Count(x => x.DiffersFromOriginal);
            _showPlan = EditorGUILayout.Foldout(_showPlan,
                _plan.ChangeCount > 0 ? $"未反映の変更（{_plan.ChangeCount}）"
                    : $"適用済みの変更（{applied}）", true);
            if (_showPlan)
            {
                foreach (var w in _plan.warnings.Distinct()) EditorGUILayout.HelpBox(w, MessageType.Warning);
                if (applied == 0 && _plan.ChangeCount == 0)
                    EditorGUILayout.LabelField("なし", EditorStyles.miniLabel);
                BoneSyncGUI.DrawPlanTable(_plan);
            }

            EditorGUILayout.Space();
            _showChanges = EditorGUILayout.Foldout(_showChanges, $"アバターの変更済みボーン（{_changes.Count}）", true);
            if (_showChanges)
            {
                if (_changes.Count == 0)
                    EditorGUILayout.LabelField("なし（ルートの Scale は対象外）", EditorStyles.wordWrappedMiniLabel);
                BoneSyncGUI.DrawAvatarChanges(root, _changes);
                if (_changes.Any(x => !x.outfitHasBone))
                    EditorGUILayout.HelpBox(
                        "－: 衣装に該当ボーンなし。そのボーン自体の変更は追従対象外（親ボーンの変更は追従）。",
                        MessageType.Info);
            }
        }
    }
}
