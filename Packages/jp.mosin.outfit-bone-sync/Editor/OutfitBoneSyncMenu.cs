using System.Linq;
using UnityEditor;
using UnityEngine;

namespace OutfitBoneSync.Editor
{
    public static class OutfitBoneSyncMenu
    {
        // メニューからはウィンドウを開くだけ（どう変わるかを見てから、ウィンドウのボタンで適用する）
        [MenuItem("Tools/Outfit Bone Sync", false, 0)]
        static void OpenWindow() => OutfitBoneSyncWindow.OpenForSelection();

        [MenuItem("GameObject/Outfit Bone Sync", false, 0)]
        static void OpenWindowFromHierarchy() => OutfitBoneSyncWindow.OpenForSelection();

        /// <summary>
        /// スクリプトから使う: 衣装にコンポーネントを付けて（無ければ）、今すぐ合わせる（Ctrl+Z で取り消せる）。
        /// 画面からはウィンドウを使う。
        /// </summary>
        public static OutfitBoneSync Fit(GameObject outfit)
        {
            var c = outfit.GetComponent<OutfitBoneSync>();
            if (c == null) c = Undo.AddComponent<OutfitBoneSync>(outfit);
            var r = BoneSyncCore.Apply(c, true);
            OutfitBoneSyncAutoFollow.Forget(c);
            Report(c, r);
            return c;
        }

        public static void Report(OutfitBoneSync c, SyncResult r)
        {
            foreach (var w in r.warnings.Distinct()) Debug.LogWarning($"[OutfitBoneSync] {c.name}: {w}", c);
            if (r.avatarRoot == null || r.pairCount == 0) return;
            Debug.Log($"[OutfitBoneSync] {c.name}: 適用（対象アバター {r.avatarRoot.name}、対応ボーン {r.pairCount}、変更 {r.changedCount}）", c);
        }
    }
}
