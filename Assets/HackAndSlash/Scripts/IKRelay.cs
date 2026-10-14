using UnityEngine;

namespace HackAndSlash
{
    /// <summary>
    /// Lives next to the Animator. Pins the hands to the procedural weapon grip with humanoid IK,
    /// turns head/torso toward the swing, and receives the locomotion clips' animation events.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class IKRelay : MonoBehaviour
    {
        public WeaponRig rig;
        public bool isPlayer;
        Animator anim;
        Transform spine, chest;
        Quaternion spineAnim, chestAnim;
        Vector3 spineUp, chestUp, spineRight, chestRight;
        bool haveAnim;

        void Awake()
        {
            anim = GetComponent<Animator>();
            spine = anim.GetBoneTransform(HumanBodyBones.Spine);
            chest = anim.GetBoneTransform(HumanBodyBones.Chest);
            Transform root = transform.parent ? transform.parent : transform;
            // twist/bend axes expressed in each bone's local space (taken from the bind pose)
            if (spine) { spineUp = spine.InverseTransformDirection(root.up); spineRight = spine.InverseTransformDirection(root.right); }
            if (chest) { chestUp = chest.InverseTransformDirection(root.up); chestRight = chest.InverseTransformDirection(root.right); }
        }

        void LateUpdate()
        {
            // remember the animated (unmodified) spine pose while no procedural twist is applied
            if (rig == null || (Mathf.Abs(rig.torsoTwist) < 0.05f && Mathf.Abs(rig.torsoBend) < 0.05f))
            {
                if (spine) spineAnim = spine.localRotation;
                if (chest) chestAnim = chest.localRotation;
                haveAnim = true;
            }
        }

        void OnAnimatorIK(int layerIndex)
        {
            if (rig == null || rig.weapon == null || anim == null) return;
            anim.SetIKPositionWeight(AvatarIKGoal.RightHand, rig.rightHandWeight);
            anim.SetIKPosition(AvatarIKGoal.RightHand, rig.GripWorld);

            if (haveAnim && (Mathf.Abs(rig.torsoTwist) > 0.05f || Mathf.Abs(rig.torsoBend) > 0.05f))
            {
                if (spine) anim.SetBoneLocalRotation(HumanBodyBones.Spine, spineAnim * Quaternion.AngleAxis(rig.torsoTwist * 0.4f, spineUp) * Quaternion.AngleAxis(rig.torsoBend * 0.5f, spineRight));
                if (chest) anim.SetBoneLocalRotation(HumanBodyBones.Chest, chestAnim * Quaternion.AngleAxis(rig.torsoTwist * 0.6f, chestUp) * Quaternion.AngleAxis(rig.torsoBend * 0.5f, chestRight));
            }

            anim.SetIKPositionWeight(AvatarIKGoal.LeftHand, rig.leftHandWeight);
            if (rig.leftHandWeight > 0.001f)
            {
                float s = rig.transform.lossyScale.y;
                anim.SetIKPosition(AvatarIKGoal.LeftHand, rig.leftHandTarget ? rig.leftHandTarget.position : rig.GripWorld - rig.BladeDirWorld * (0.14f * s));
            }

            anim.SetLookAtWeight(rig.lookWeight, 0.4f, 0.8f, 0f, 0.5f);
            if (rig.lookWeight > 0.001f) anim.SetLookAtPosition(rig.lookTargetWorld);
        }

        // Animation events baked into the Starter Assets locomotion clips.
        void OnFootstep(AnimationEvent e)
        {
            if (isPlayer && e.animatorClipInfo.weight > 0.5f) Sfx.Footstep();
        }

        void OnLand(AnimationEvent e) { }
    }
}
