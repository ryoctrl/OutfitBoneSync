using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace OutfitBoneSync.Editor
{
    /// <summary>
    /// 編集中にアバターのボーンの値が変わったら、「自動で合わせ直す」がオンの衣装を合わせ直す。
    /// Play Mode とコンパイル中は何もしない。シーンには何も足さない。
    /// </summary>
    [InitializeOnLoad]
    static class OutfitBoneSyncAutoFollow
    {
        const double Interval = 0.25;
        static double _next;
        static readonly Dictionary<int, int> Hashes = new Dictionary<int, int>();

        static OutfitBoneSyncAutoFollow()
        {
            EditorApplication.update += Tick;
        }

        /// <summary>次の確認で、このコンポーネントを必ず合わせ直すようにする。</summary>
        public static void Forget(OutfitBoneSync c) => Hashes.Remove(c.GetInstanceID());

        static void Tick()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
                EditorApplication.isUpdating) return;
            var now = EditorApplication.timeSinceStartup;
            if (now < _next) return;
            _next = now + Interval;

            var all = Object.FindObjectsByType<OutfitBoneSync>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var c in all)
            {
                if (!c.autoFollow || EditorUtility.IsPersistent(c)) continue;
                if ((c.hideFlags & HideFlags.NotEditable) != 0) continue;
                var h = BoneSyncCore.AvatarPoseHash(c, out var valid);
                if (!valid) continue;
                var id = c.GetInstanceID();
                if (Hashes.TryGetValue(id, out var old) && old == h) continue;
                BoneSyncCore.Apply(c, false);
                // 合わせた結果で控えが増えると値が変わるので、合わせたあとの値で覚え直す
                Hashes[id] = BoneSyncCore.AvatarPoseHash(c, out _);
            }
        }
    }
}
