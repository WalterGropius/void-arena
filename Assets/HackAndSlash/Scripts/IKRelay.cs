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

        void Awake() => anim = GetComponent<Animator>();

        void OnAnimatorIK(int layerIndex)
        {
            if (rig == null || rig.weapon == null || anim == null) return;
            anim.SetIKPositionWeight(AvatarIKGoal.RightHand, rig.rightHandWeight);
            anim.SetIKPosition(AvatarIKGoal.RightHand, rig.GripWorld);

            anim.SetIKPositionWeight(AvatarIKGoal.LeftHand, rig.leftHandWeight);
            if (rig.leftHandWeight > 0.001f)
            {
                float s = rig.transform.lossyScale.y;
                anim.SetIKPosition(AvatarIKGoal.LeftHand, rig.GripWorld - rig.BladeDirWorld * (0.14f * s));
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
