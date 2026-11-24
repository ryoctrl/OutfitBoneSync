using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
#if OBS_MA
using nadena.dev.modular_avatar.core;
#endif

namespace OutfitBoneSync.Editor
{
    /// <summary>衣装のボーンと、それに対応するアバターのボーン。</summary>
    public struct BonePair
    {
        public Transform outfit;
        public Transform avatar;
    }

    /// <summary>アバターのボーン 1 本の、改変前からの違い。</summary>
    public class AvatarBoneChange
    {
        public Transform bone;
        public Vector3 positionDelta;
        public float rotationAngle;
        public Vector3 scaleRatio;
        public bool outfitHasBone;
    }

    /// <summary>基準アバターに同じパスのボーンが無く、別の取得元の値を使ったアバターのボーン。</summary>
    public class ReferenceMiss
    {
        public Transform bone;
        public string source;
    }

    public class SyncResult
    {
        public Transform avatarRoot;
        public int pairCount;
        public int changedCount;
        public readonly List<string> warnings = new List<string>();
    }

    /// <summary>衣装のボーン 1 本の、合わせる前・今・合わせたあとの Local の値。</summary>
    public class BonePlan
    {
        public Transform bone;
        /// <summary>対応するアバターのボーン。控えだけ残っていて対応が無いボーンは null（合わせる前の値に戻す）。</summary>
        public Transform avatar;
        public Vector3 origPosition, curPosition, newPosition;
        public Quaternion origRotation, curRotation, newRotation;
        public Vector3 origScale, curScale, newScale;
        /// <summary>まだ控えていないボーン（適用すると今の値を合わせる前の値として控える）。</summary>
        public bool newRecord;

        public bool Changes => curPosition != newPosition || curRotation != newRotation || curScale != newScale;
        public bool DiffersFromOriginal => origPosition != newPosition || origRotation != newRotation || origScale != newScale;
    }

    /// <summary>衣装を合わせたら、どのボーンがどう変わるか（シーンは変えない）。</summary>
    public class SyncPlan
    {
        public Transform avatarRoot;
        public Transform outfitRoot;
        public int pairCount;
        public string referenceDescription = "";
        public readonly List<BonePlan> bones = new List<BonePlan>();
        public readonly List<string> warnings = new List<string>();
        public int ChangeCount => bones.Count(x => x.Changes);
    }

    /// <summary>
    /// 衣装のボーンを、アバターのボーンの改変に合わせる処理。
    ///
    /// アバターのボーン a の「改変前のワールド行列」を Rw、「今のワールド行列」を Cw とすると、
    /// 体のメッシュは Cw * Rw^-1 だけ動いている。衣装のボーン o の合わせる前のワールド行列を Ow として、
    /// 衣装のボーンを Cw * Rw^-1 * Ow に置く。衣装のボーンがアバターと同じ Position・Rotation なら、アバターのボーンの
    /// Position・Rotation・Scale をそのまま写したのと同じ値になり、衣装のボーンが少し違う場合も
    /// その違いを保ったまま同じだけ動かす。
    /// </summary>
    public static class BoneSyncCore
    {
        const float PosEps = 1e-6f;
        const float ScaleEps = 1e-5f;
        const float AngleEps = 0.001f;

        public static Transform FindAvatarRoot(OutfitBoneSync c)
        {
            if (c.avatarRoot != null) return c.avatarRoot;
            return FindAvatarRoot(c.transform);
        }

        /// <summary>衣装の親をたどって、アバターのルートを探す。</summary>
        public static Transform FindAvatarRoot(Transform outfit)
        {
            var parent = outfit.parent;
            if (parent == null) return null;
            var desc = parent.GetComponentInParent<VRC.SDKBase.VRC_AvatarDescriptor>(true);
            if (desc != null) return desc.transform;
            for (var t = parent; t != null; t = t.parent)
            {
                var anim = t.GetComponent<Animator>();
                if (anim != null && anim.avatar != null && anim.avatar.isHuman) return t;
            }
            return null;
        }

        // ---- ボーンの対応付け（Modular Avatar の Merge Armature と同じ、名前による親子のたどり方） ----

        public static List<BonePair> Match(OutfitBoneSync c, Transform avatarRoot, List<string> warnings)
            => Match(c.transform, avatarRoot, warnings);

        public static List<BonePair> Match(Transform c, Transform avatarRoot, List<string> warnings)
        {
            var pairs = new List<BonePair>();
            var seen = new HashSet<Transform>();
#if OBS_MA
            var merges = c.GetComponentsInChildren<ModularAvatarMergeArmature>(true);
            foreach (var ma in merges)
            {
                var target = ma.mergeTargetObject;
                if (target == null)
                {
                    warnings.Add($"{ma.name}: Merge Armature の Merge Target が未設定");
                    continue;
                }
                if (!target.transform.IsChildOf(avatarRoot))
                {
                    warnings.Add($"{ma.name}: Merge Armature の Merge Target が対象アバター外");
                    continue;
                }
                Zip(ma.transform, target.transform, ma.prefix ?? "", ma.suffix ?? "", pairs, seen);
            }
            if (merges.Length > 0) return pairs;
#endif
            // Merge Armature が無い衣装は、アバターの Hips と同じ名前のボーンから合わせる
            var anim = avatarRoot.GetComponent<Animator>();
            var hips = anim != null && anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.Hips) : null;
            if (hips == null)
            {
                warnings.Add("Animator に Hips ボーンなし");
                return pairs;
            }
            var oHips = c.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == hips.name);
            if (oHips == null)
            {
                warnings.Add($"衣装に Hips 相当のボーン（{hips.name}）なし");
                return pairs;
            }
            var aArm = hips.parent;
            var oArm = oHips.parent;
            if (aArm != null && aArm != avatarRoot && oArm != null && oArm != c.parent && oArm.name == aArm.name)
                Zip(oArm, aArm, "", "", pairs, seen);
            else
                Zip(oHips, hips, "", "", pairs, seen);
            return pairs;
        }

        static void Zip(Transform o, Transform a, string prefix, string suffix, List<BonePair> pairs, HashSet<Transform> seen)
        {
            if (!seen.Add(o)) return;
            pairs.Add(new BonePair { outfit = o, avatar = a });
            foreach (Transform oc in o)
            {
#if OBS_MA
                if (oc.GetComponent<ModularAvatarMergeArmature>() != null) continue;
#endif
                var n = oc.name;
                if (!n.StartsWith(prefix) || !n.EndsWith(suffix) || n.Length <= prefix.Length + suffix.Length) continue;
                var targetName = n.Substring(prefix.Length, n.Length - prefix.Length - suffix.Length);
                Transform ac = null;
                foreach (Transform child in a)
                    if (child.name == targetName) { ac = child; break; }
                if (ac != null) Zip(oc, ac, prefix, suffix, pairs, seen);
            }
        }

        // ---- 改変前のアバターのボーンの値 ----

        /// <summary>
        /// 改変前のボーンの値を、次の順で探す。
        /// 1. 指定された「改変前のアバター」の同じパスのボーン
        /// 2. プレハブの大元の FBX の同じボーン（アバターが FBX から作ったプレハブのとき）
        /// 3. アバターの体のメッシュが入っている FBX の同じパスのボーン（プレハブを展開してあるときや、アップロードの処理中）
        /// 4. Animator の Avatar の skeleton（FBX が見つからないとき。指の Rotation などが FBX と違うことがあるので最後に使う）
        /// どれも無いボーンは、今の値を改変前とみなす（そのボーンは変えていない扱い）。
        /// </summary>
        public class ReferencePose
        {
            readonly Transform _avatarRoot;
            readonly Transform _refRoot;
            readonly Transform _modelRoot;
            readonly Dictionary<string, SkeletonBone> _skeleton = new Dictionary<string, SkeletonBone>();
            readonly HashSet<string> _duplicated = new HashSet<string>();
            readonly Dictionary<Transform, Matrix4x4> _rootSpace = new Dictionary<Transform, Matrix4x4>();

            public string Description { get; }

            public ReferencePose(Transform avatarRoot, GameObject referenceAvatar)
            {
                _avatarRoot = avatarRoot;
                _refRoot = referenceAvatar != null ? referenceAvatar.transform : null;
                var anim = avatarRoot.GetComponent<Animator>();
                var hips = anim != null && anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.Hips) : null;
                if (hips != null) _modelRoot = FindBodyModel(avatarRoot, hips);
                if (anim != null && anim.avatar != null)
                {
                    foreach (var sb in anim.avatar.humanDescription.skeleton)
                    {
                        if (_skeleton.ContainsKey(sb.name)) _duplicated.Add(sb.name);
                        else _skeleton[sb.name] = sb;
                    }
                }
                Description = _refRoot != null ? $"基準アバター {_refRoot.name}"
                    : _modelRoot != null ? $"FBX {_modelRoot.name}"
                    : _skeleton.Count > 0 ? "Animator の Avatar（skeleton）"
                    : "なし";
            }

            /// <summary>アバターの Hips を使っているメッシュが入っている FBX（同じパスに Hips があるもの）。</summary>
            static Transform FindBodyModel(Transform avatarRoot, Transform hips)
            {
                var hipsPath = RelativePath(avatarRoot, hips);
                if (hipsPath == null) return null;
                foreach (var smr in avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (smr.sharedMesh == null || !smr.bones.Contains(hips)) continue;
                    var path = AssetDatabase.GetAssetPath(smr.sharedMesh);
                    if (string.IsNullOrEmpty(path) || !(AssetImporter.GetAtPath(path) is ModelImporter)) continue;
                    var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (model != null && model.transform.Find(hipsPath) != null) return model.transform;
                }
                return null;
            }

            public bool HasReference => _refRoot != null;

            /// <summary>
            /// ボーン t の改変前の Local の値。分からなければ今の値を返す（そのボーンは変えていないとみなす）。
            /// 戻り値は値の取得元。
            /// </summary>
            public string GetLocal(Transform t, out Vector3 p, out Quaternion r, out Vector3 s)
            {
                var path = RelativePath(_avatarRoot, t);
                if (_refRoot != null && path != null)
                {
                    var rt = _refRoot.Find(path);
                    if (rt != null)
                    {
                        p = rt.localPosition; r = rt.localRotation; s = rt.localScale;
                        return SourceReference;
                    }
                }
                var src = PrefabUtility.GetCorrespondingObjectFromOriginalSource(t);
                if (src != null && src != t && PrefabUtility.GetPrefabAssetType(src) == PrefabAssetType.Model)
                {
                    p = src.localPosition; r = src.localRotation; s = src.localScale;
                    return $"FBX {src.root.name}";
                }
                if (_modelRoot != null && path != null)
                {
                    var mt = _modelRoot.Find(path);
                    if (mt != null)
                    {
                        p = mt.localPosition; r = mt.localRotation; s = mt.localScale;
                        return $"FBX {_modelRoot.name}";
                    }
                }
                if (_skeleton.TryGetValue(t.name, out var sb) && !_duplicated.Contains(t.name))
                {
                    p = sb.position; r = sb.rotation; s = sb.scale;
                    return "Animator の Avatar（skeleton）";
                }
                p = t.localPosition; r = t.localRotation; s = t.localScale;
                return "現在値（変更なしとみなす）";
            }

            public const string SourceReference = "基準アバター";

            /// <summary>改変前の、アバターのルートから見たボーン t の行列。</summary>
            public Matrix4x4 RootSpace(Transform t)
            {
                if (t == _avatarRoot) return Matrix4x4.identity;
                if (_rootSpace.TryGetValue(t, out var m)) return m;
                GetLocal(t, out var p, out var r, out var s);
                var parent = t.parent != null ? RootSpace(t.parent) : Matrix4x4.identity;
                m = parent * Matrix4x4.TRS(p, r, s);
                _rootSpace[t] = m;
                return m;
            }

            /// <summary>改変前の位置から今の位置までの、ワールド空間での動き。</summary>
            public Matrix4x4 Delta(Transform t)
            {
                var refWorld = _avatarRoot.localToWorldMatrix * RootSpace(t);
                return t.localToWorldMatrix * refWorld.inverse;
            }
        }

        static string RelativePath(Transform root, Transform t)
        {
            var names = new List<string>();
            for (var x = t; x != root; x = x.parent)
            {
                if (x == null) return null;
                names.Add(x.name);
            }
            names.Reverse();
            return string.Join("/", names);
        }

        // ---- 合わせる・戻す ----

        /// <summary>
        /// 衣装を合わせたら、どのボーンがどう変わるかを計算する（シーンは変えない）。
        /// records は、すでに控えてある合わせる前の値（まだ合わせていない衣装なら null か空）。
        /// </summary>
        public static SyncPlan Plan(Transform outfitRoot, Transform avatarRoot, GameObject referenceAvatar,
            List<OutfitBoneSync.BoneRecord> records)
        {
            var plan = new SyncPlan { avatarRoot = avatarRoot, outfitRoot = outfitRoot };
            if (avatarRoot == null)
            {
                plan.warnings.Add("対象アバターなし。衣装をアバターの子に配置、またはアバターを指定。");
                return plan;
            }
            var pairs = Match(outfitRoot, avatarRoot, plan.warnings);
            plan.pairCount = pairs.Count;
            var refPose = new ReferencePose(avatarRoot, referenceAvatar);
            plan.referenceDescription = refPose.Description;
            if (pairs.Count == 0) return plan;

            // 合わせる前の値: 控えがあればその値、無ければ今の値
            var map = new Dictionary<Transform, BonePlan>();
            if (records != null)
            {
                foreach (var rec in records)
                {
                    if (rec.bone == null || map.ContainsKey(rec.bone)) continue;
                    map[rec.bone] = new BonePlan
                    {
                        bone = rec.bone,
                        origPosition = rec.localPosition, origRotation = rec.localRotation, origScale = rec.localScale,
                    };
                }
            }
            foreach (var pair in pairs)
            {
                if (map.TryGetValue(pair.outfit, out var known))
                {
                    known.avatar = pair.avatar;
                    continue;
                }
                var o = pair.outfit;
                var bp = new BonePlan
                {
                    bone = o, avatar = pair.avatar, newRecord = true,
                    origPosition = o.localPosition, origRotation = o.localRotation, origScale = o.localScale,
                };
                // 手で（または MA の「位置を元アバターに合わせてリセット」で）すでにアバターの改変後の値に
                // 合わせてあるボーンは、改変前の値を元として控える。そのまま控えると改変が二重にかかるため。
                refPose.GetLocal(pair.avatar, out var rp, out var rr, out var rs);
                var a = pair.avatar;
                bool avatarChanged = !Same(a.localPosition, a.localRotation, a.localScale, rp, rr, rs);
                bool outfitEqualsCurrent = Same(bp.origPosition, bp.origRotation, bp.origScale,
                    a.localPosition, a.localRotation, a.localScale);
                if (avatarChanged && outfitEqualsCurrent)
                {
                    bp.origPosition = rp; bp.origRotation = rr; bp.origScale = rs;
                    plan.warnings.Add($"{o.name}: アバターの改変後の値と一致。基準値を適用前の値として記録");
                }
                map[o] = bp;
            }
            foreach (var bp in map.Values)
            {
                bp.curPosition = bp.bone.localPosition;
                bp.curRotation = bp.bone.localRotation;
                bp.curScale = bp.bone.localScale;
            }

            // いったん合わせる前の値に戻した状態を基準に、親から順に置き直す（何度合わせても同じ結果にする）
            var ctx = new PlanContext { map = map, refPose = refPose };
            foreach (var bp in map.Values) ctx.NewWorld(bp.bone);
            plan.bones.AddRange(map.Values.OrderBy(x => Depth(x.bone)));
            return plan;
        }

        class PlanContext
        {
            public Dictionary<Transform, BonePlan> map;
            public ReferencePose refPose;
            readonly Dictionary<Transform, Matrix4x4> _orig = new Dictionary<Transform, Matrix4x4>();
            readonly Dictionary<Transform, Matrix4x4> _new = new Dictionary<Transform, Matrix4x4>();

            /// <summary>衣装のボーンを合わせる前の値に戻したときのワールド行列。</summary>
            public Matrix4x4 OrigWorld(Transform t)
            {
                if (t == null) return Matrix4x4.identity;
                if (_orig.TryGetValue(t, out var m)) return m;
                m = OrigWorld(t.parent) * (map.TryGetValue(t, out var bp)
                    ? Matrix4x4.TRS(bp.origPosition, bp.origRotation, bp.origScale)
                    : Matrix4x4.TRS(t.localPosition, t.localRotation, t.localScale));
                _orig[t] = m;
                return m;
            }

            /// <summary>合わせたあとのワールド行列。対応するボーンは、ここで合わせたあとの Local の値を決める。</summary>
            public Matrix4x4 NewWorld(Transform t)
            {
                if (t == null) return Matrix4x4.identity;
                if (_new.TryGetValue(t, out var m)) return m;
                var parent = NewWorld(t.parent);
                if (!map.TryGetValue(t, out var bp))
                {
                    m = parent * Matrix4x4.TRS(t.localPosition, t.localRotation, t.localScale);
                }
                else if (bp.avatar == null)
                {
                    bp.newPosition = bp.origPosition; bp.newRotation = bp.origRotation; bp.newScale = bp.origScale;
                    m = parent * Matrix4x4.TRS(bp.newPosition, bp.newRotation, bp.newScale);
                }
                else
                {
                    var target = refPose.Delta(bp.avatar) * OrigWorld(t);
                    Decompose(parent.inverse * target, out var p, out var r, out var s);

                    // 計算の誤差で値が細かく揺れないように、元の値・アバターのボーンの値に近ければその値にする
                    var a = bp.avatar;
                    if (Same(p, r, s, bp.origPosition, bp.origRotation, bp.origScale))
                    {
                        p = bp.origPosition; r = bp.origRotation; s = bp.origScale;
                    }
                    else if (Same(p, r, s, a.localPosition, a.localRotation, a.localScale))
                    {
                        p = a.localPosition; r = a.localRotation; s = a.localScale;
                    }
                    bp.newPosition = p; bp.newRotation = r; bp.newScale = s;
                    m = parent * Matrix4x4.TRS(p, r, s);
                }
                _new[t] = m;
                return m;
            }
        }

        /// <summary>衣装のボーンをアバターのボーンの改変に合わせる。recordUndo が true なら Undo に積む。</summary>
        public static SyncResult Apply(OutfitBoneSync c, bool recordUndo)
        {
            var result = new SyncResult();
            var root = FindAvatarRoot(c);
            result.avatarRoot = root;
            var plan = Plan(c.transform, root, c.referenceAvatar, c.records);
            result.warnings.AddRange(plan.warnings);
            result.pairCount = plan.pairCount;
            if (root == null || plan.pairCount == 0) return result;

            if (recordUndo)
            {
                Undo.RecordObject(c, "Outfit Bone Sync");
                Undo.RecordObjects(plan.bones.Select(x => (Object)x.bone).ToArray(), "Outfit Bone Sync");
            }

            // 合わせる前の値を控える（初めて見たボーンだけ）
            int removed = c.records.RemoveAll(x => x.bone == null);
            bool recordsChanged = removed > 0;
            foreach (var bp in plan.bones.Where(x => x.newRecord))
            {
                c.records.Add(new OutfitBoneSync.BoneRecord
                {
                    bone = bp.bone,
                    localPosition = bp.origPosition, localRotation = bp.origRotation, localScale = bp.origScale,
                });
                recordsChanged = true;
            }

            foreach (var bp in plan.bones.Where(x => x.Changes))
            {
                SetLocal(bp.bone, bp.newPosition, bp.newRotation, bp.newScale);
                result.changedCount++;
                MarkDirty(bp.bone);
            }
            if (recordsChanged) MarkDirty(c);
            return result;
        }

        /// <summary>
        /// 衣装のボーンを合わせる前の値に戻し、このコンポーネントを外す（1 回の Ctrl+Z で取り消せる）。
        /// プレハブに元から入っていて外せないときは、戻して「自動で合わせ直す」をオフにする。
        /// </summary>
        public static int RevertAndRemove(OutfitBoneSync c)
        {
            Undo.IncrementCurrentGroup();
            var group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Outfit Bone Sync 元に戻す");
            Undo.RecordObject(c, "Outfit Bone Sync 元に戻す");
            c.autoFollow = false;
            var n = Restore(c, true);
            OutfitBoneSyncAutoFollow.Forget(c);
            bool removable = !PrefabUtility.IsPartOfPrefabInstance(c) || PrefabUtility.IsAddedComponentOverride(c);
            if (removable) Undo.DestroyObjectImmediate(c);
            Undo.CollapseUndoOperations(group);
            return n;
        }

        /// <summary>アバターの直下で、衣装らしい子（Merge Armature を持つ、このツールが付いている、Hips と同じ名前のボーンがある）。</summary>
        public static List<Transform> FindOutfitCandidates(Transform avatarRoot)
        {
            var list = new List<Transform>();
            if (avatarRoot == null) return list;
            var anim = avatarRoot.GetComponent<Animator>();
            var hips = anim != null && anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.Hips) : null;
            foreach (Transform child in avatarRoot)
            {
                if (hips != null && hips.IsChildOf(child)) continue; // アバター本体の Armature
                bool isOutfit = child.GetComponent<OutfitBoneSync>() != null;
#if OBS_MA
                isOutfit |= child.GetComponentInChildren<ModularAvatarMergeArmature>(true) != null;
#endif
                if (!isOutfit && hips != null)
                    isOutfit = child.GetComponentsInChildren<Transform>(true).Any(t => t != child && t.name == hips.name);
                if (isOutfit) list.Add(child);
            }
            return list;
        }

        /// <summary>衣装のボーンを、合わせる前の値に戻して控えを消す。戻り値は値が変わったボーンの数。</summary>
        public static int Restore(OutfitBoneSync c, bool recordUndo)
        {
            var bones = c.records.Where(x => x.bone != null).Select(x => x.bone).ToArray();
            if (recordUndo)
            {
                Undo.RecordObject(c, "Outfit Bone Sync Restore");
                Undo.RecordObjects(bones, "Outfit Bone Sync Restore");
            }
            int n = 0;
            foreach (var rec in c.records)
            {
                if (rec.bone == null) continue;
                var t = rec.bone;
                // Unity の == は誤差を許すので、元の値にきっちり戻すために Equals で比べる
                if (t.localPosition.Equals(rec.localPosition) && t.localRotation.Equals(rec.localRotation) &&
                    t.localScale.Equals(rec.localScale))
                    continue;
                SetLocal(t, rec.localPosition, rec.localRotation, rec.localScale);
                MarkDirty(t);
                n++;
            }
            c.records.Clear();
            MarkDirty(c);
            return n;
        }

        /// <summary>アバターのボーンのうち、改変前から値が変わっているもの（衣装に対応するボーンがあるかも付ける）。</summary>
        public static List<AvatarBoneChange> ListAvatarChanges(OutfitBoneSync c)
        {
            var root = FindAvatarRoot(c);
            if (root == null) return new List<AvatarBoneChange>();
            var warnings = new List<string>();
            var matched = new HashSet<Transform>(Match(c, root, warnings).Select(x => x.avatar));
            return ListAvatarChanges(root, c.referenceAvatar, matched);
        }

        /// <summary>アバターのボーンのうち、改変前から値が変わっているもの。matched に入っているボーンは「衣装にある」とする。</summary>
        public static List<AvatarBoneChange> ListAvatarChanges(Transform root, GameObject referenceAvatar, ICollection<Transform> matched)
        {
            var list = new List<AvatarBoneChange>();
            if (root == null) return list;
            var refPose = new ReferencePose(root, referenceAvatar);
            foreach (var t in AvatarBones(root))
            {
                refPose.GetLocal(t, out var p, out var r, out var s);
                if (Same(t.localPosition, t.localRotation, t.localScale, p, r, s)) continue;
                list.Add(new AvatarBoneChange
                {
                    bone = t,
                    positionDelta = t.localPosition - p,
                    rotationAngle = Quaternion.Angle(t.localRotation, r),
                    scaleRatio = new Vector3(Div(t.localScale.x, s.x), Div(t.localScale.y, s.y), Div(t.localScale.z, s.z)),
                    outfitHasBone = matched != null && matched.Contains(t),
                });
            }
            return list;
        }

        /// <summary>アバター本体のボーン（衣装・アクセサリーのボーンを除く）。</summary>
        static IEnumerable<Transform> AvatarBones(Transform root)
        {
            var anim = root.GetComponent<Animator>();
            var hips = anim != null && anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.Hips) : null;
            var armature = hips == null ? root : hips.parent != null && hips.parent != root ? hips.parent : hips;
            foreach (var t in armature.GetComponentsInChildren<Transform>(true))
            {
                if (t == root || t.GetComponentInParent<OutfitBoneSync>(true) != null) continue;
#if OBS_MA
                if (t.GetComponentInParent<ModularAvatarMergeArmature>(true) != null) continue;
#endif
                yield return t;
            }
        }

        /// <summary>
        /// 基準アバターに同じパス（アバターのルートからの階層）のボーンが無いアバターのボーンと、代わりに使った取得元。
        /// 基準アバターが未指定なら空。
        /// </summary>
        public static List<ReferenceMiss> ListReferenceMisses(Transform root, GameObject referenceAvatar)
        {
            var list = new List<ReferenceMiss>();
            if (root == null || referenceAvatar == null) return list;
            var refPose = new ReferencePose(root, referenceAvatar);
            foreach (var t in AvatarBones(root))
            {
                var source = refPose.GetLocal(t, out _, out _, out _);
                if (source != ReferencePose.SourceReference) list.Add(new ReferenceMiss { bone = t, source = source });
            }
            return list;
        }

        /// <summary>自動で合わせ直すかの判定に使う、対応するアバターのボーンとその親の値の要約。</summary>
        public static int AvatarPoseHash(OutfitBoneSync c, out bool valid)
        {
            valid = false;
            var root = FindAvatarRoot(c);
            if (root == null) return 0;
            var warnings = new List<string>();
            var pairs = Match(c, root, warnings);
            if (pairs.Count == 0) return 0;
            valid = true;
            var bones = new HashSet<Transform>();
            foreach (var pair in pairs)
                for (var t = pair.avatar; t != null && t != root; t = t.parent)
                    if (!bones.Add(t)) break;
            unchecked
            {
                int h = 17;
                h = h * 31 + pairs.Count;
                h = h * 31 + (c.referenceAvatar != null ? c.referenceAvatar.GetInstanceID() : 0);
                foreach (var t in bones.OrderBy(x => x.GetInstanceID()))
                {
                    h = h * 31 + t.localPosition.GetHashCode();
                    h = h * 31 + t.localRotation.GetHashCode();
                    h = h * 31 + t.localScale.GetHashCode();
                }
                foreach (var rec in c.records)
                    h = h * 31 + (rec.bone != null ? rec.bone.GetInstanceID() : 0);
                return h;
            }
        }

        // ---- 小さな道具 ----

        static float Div(float a, float b) => Mathf.Abs(b) < 1e-8f ? 0f : a / b;

        static bool Same(Vector3 p1, Quaternion r1, Vector3 s1, Vector3 p2, Quaternion r2, Vector3 s2)
        {
            return (p1 - p2).sqrMagnitude < PosEps * PosEps
                   && Quaternion.Angle(r1, r2) < AngleEps
                   && (s1 - s2).sqrMagnitude < ScaleEps * ScaleEps;
        }

        static int Depth(Transform t)
        {
            int d = 0;
            for (; t != null; t = t.parent) d++;
            return d;
        }

        static void SetLocal(Transform t, Vector3 p, Quaternion r, Vector3 s)
        {
            if (!t.localPosition.Equals(p)) t.localPosition = p;
            if (!t.localRotation.Equals(r)) t.localRotation = r;
            if (!t.localScale.Equals(s)) t.localScale = s;
        }

        static void Decompose(Matrix4x4 m, out Vector3 p, out Quaternion r, out Vector3 s)
        {
            p = m.GetColumn(3);
            Vector3 c0 = m.GetColumn(0), c1 = m.GetColumn(1), c2 = m.GetColumn(2);
            s = new Vector3(c0.magnitude, c1.magnitude, c2.magnitude);
            if (Vector3.Dot(Vector3.Cross(c0, c1), c2) < 0) s.x = -s.x;
            r = Quaternion.LookRotation(c2 / s.z, c1 / s.y);
        }

        static void MarkDirty(Object o)
        {
            EditorUtility.SetDirty(o);
            if (PrefabUtility.IsPartOfPrefabInstance(o))
                PrefabUtility.RecordPrefabInstancePropertyModifications(o);
        }
    }
}
