#if OBS_NDMF
using nadena.dev.ndmf;
using OutfitBoneSync.Editor;
using UnityEngine;

[assembly: ExportsPlugin(typeof(OutfitBoneSyncPlugin))]

namespace OutfitBoneSync.Editor
{
    /// <summary>
    /// アップロード時（と Play Mode）に、Modular Avatar が衣装のボーンをアバターに結合する前に、
    /// 衣装のボーンをもう一度アバターのボーンの改変に合わせてから、このコンポーネントを消す。
    /// MA は結合のとき衣装の今の見た目を保つので、ここで合わせておけば改変後の体に合った形で結合される。
    /// </summary>
    public class OutfitBoneSyncPlugin : Plugin<OutfitBoneSyncPlugin>
    {
        public override string QualifiedName => "outfitbonesync";
        public override string DisplayName => "Outfit Bone Sync（衣装のボーン合わせ）";

        protected override void Configure()
        {
            var seq = InPhase(BuildPhase.Resolving);
#if OBS_MA
            seq = seq.BeforePlugin("nadena.dev.modular-avatar");
#endif
            seq.Run("Fit outfit bones to avatar", ctx =>
            {
                foreach (var c in ctx.AvatarRootObject.GetComponentsInChildren<OutfitBoneSync>(true))
                {
                    if (c.avatarRoot == null) c.avatarRoot = ctx.AvatarRootTransform;
                    var result = BoneSyncCore.Apply(c, false);
                    foreach (var w in result.warnings)
                        Debug.LogWarning($"[OutfitBoneSync] {c.name}: {w}", c);
                    Object.DestroyImmediate(c);
                }
            });
        }
    }
}
#endif
