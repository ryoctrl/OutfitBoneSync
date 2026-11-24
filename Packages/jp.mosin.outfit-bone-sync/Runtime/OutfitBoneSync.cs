using System;
using System.Collections.Generic;
using UnityEngine;

namespace OutfitBoneSync
{
    /// <summary>
    /// 衣装のボーンを、アバターのボーンの改変（Position・Rotation・Scale を元のアバターから変えた分）に合わせる。
    /// 衣装のルートに付ける。合わせる前の衣装のボーンの値は records に控え、何度合わせても同じ結果になり、元にも戻せる。
    /// アップロード時（と Play Mode）は、NDMF があれば Modular Avatar の処理より前にもう一度合わせてから、このコンポーネントを消す。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Outfit Bone Sync/衣装のボーン合わせ")]
    public class OutfitBoneSync : MonoBehaviour, VRC.SDKBase.IEditorOnly
    {
        [Tooltip("対象アバターのルート。未指定時は親の VRC Avatar Descriptor を使用")]
        public Transform avatarRoot;

        [Tooltip("基準アバター（配布元プレハブなど、改変前のアバター）。未指定時は元 FBX のボーン値を使用")]
        public GameObject referenceAvatar;

        [Tooltip("自動追従: アバターのボーン変更時に衣装へ再適用（編集時）")]
        public bool autoFollow = true;

        [Tooltip("適用前の衣装ボーンの値（自動記録）")]
        public List<BoneRecord> records = new List<BoneRecord>();

        [Serializable]
        public class BoneRecord
        {
            public Transform bone;
            public Vector3 localPosition;
            public Quaternion localRotation;
            public Vector3 localScale;
        }
    }
}
