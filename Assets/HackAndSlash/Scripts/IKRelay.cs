using UnityEngine;

namespace HackAndSlash
{
    /// <summary>
    /// Lives next to the Animator and turns the <see cref="WeaponRig"/>'s procedural layer into a pose: hands pinned to
    /// the weapon grip (and the off hand to a shield, a blaster aim or a tether), feet thrown into kicks, hips dropped
    /// and leaned, torso twisted into swings and snapped back by hits. Also receives the locomotion clips' events.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class IKRelay : MonoBehaviour
    {
        public WeaponRig rig;
        public bool isPlayer;
        Animator anim;
        Transform spine, chest, root;
        Quaternion spineAnim, chestAnim;
        Vector3 spineUp, chestUp, spineRight, chestRight;
        bool haveAnim;

        void Awake()
        {
            anim = GetComponent<Animator>();
            spine = anim.GetBoneTransform(HumanBodyBones.Spine);
            chest = anim.GetBoneTransform(HumanBodyBones.Chest);
            root = transform.parent ? transform.parent : transform;
            // twist/bend axes expressed in each bone's local space (taken from the bind pose)
            if (spine) { spineUp = spine.InverseTransformDirection(root.up); spineRight = spine.InverseTransformDirection(root.right); }
            if (chest) { chestUp = chest.InverseTransformDirection(root.up); chestRight = chest.InverseTransformDirection(root.right); }
        }

        bool TorsoActive => rig != null && (Mathf.Abs(rig.torsoTwist + rig.ReactTwist) > 0.05f || Mathf.Abs(rig.torsoBend + rig.ReactPitch) > 0.05f);

        void LateUpdate()
        {
            // remember the animated (unmodified) spine pose while no procedural twist is applied
            if (!TorsoActive)
            {
                if (spine) spineAnim = spine.localRotation;
                if (chest) chestAnim = chest.localRotation;
                haveAnim = true;
            }
        }

        void OnAnimatorIK(int layerIndex)
        {
            if (rig == null || anim == null) return;

            // feet first: remember where the animation put them, so moving the body can leave them planted
            Vector3 lf = anim.GetIKPosition(AvatarIKGoal.LeftFoot), rf = anim.GetIKPosition(AvatarIKGoal.RightFoot);
            Quaternion lfr = anim.GetIKRotation(AvatarIKGoal.LeftFoot), rfr = anim.GetIKRotation(AvatarIKGoal.RightFoot);

            // whole-body layer: drop / lift / lean the hips
            if (rig.bodyOffset.sqrMagnitude > 1e-6f || Mathf.Abs(rig.bodyPitch) > 0.05f || Mathf.Abs(rig.bodyRoll) > 0.05f)
            {
                float s = root.lossyScale.y;
                anim.bodyPosition += root.rotation * (rig.bodyOffset * s);
                anim.bodyRotation = Quaternion.AngleAxis(rig.bodyPitch, root.right) * Quaternion.AngleAxis(-rig.bodyRoll, root.forward) * anim.bodyRotation;
            }

            // left foot: kick goal, else planted
            float lw = Mathf.Max(rig.leftFootWeight, rig.plantFeet);
            anim.SetIKPositionWeight(AvatarIKGoal.LeftFoot, lw);
            anim.SetIKRotationWeight(AvatarIKGoal.LeftFoot, rig.plantFeet * (1f - rig.leftFootWeight));
            if (lw > 0.001f)
            {
                anim.SetIKPosition(AvatarIKGoal.LeftFoot, Vector3.Lerp(lf, rig.leftFootTarget, rig.leftFootWeight / Mathf.Max(lw, 1e-4f)));
                anim.SetIKRotation(AvatarIKGoal.LeftFoot, lfr);
                if (rig.leftFootWeight > 0.01f) { anim.SetIKHintPositionWeight(AvatarIKHint.LeftKnee, rig.leftFootWeight); anim.SetIKHintPosition(AvatarIKHint.LeftKnee, rig.leftKneeHint); }
                else anim.SetIKHintPositionWeight(AvatarIKHint.LeftKnee, 0f);
            }
            float rw = Mathf.Max(rig.rightFootWeight, rig.plantFeet);
            anim.SetIKPositionWeight(AvatarIKGoal.RightFoot, rw);
            anim.SetIKRotationWeight(AvatarIKGoal.RightFoot, rig.plantFeet * (1f - rig.rightFootWeight));
            if (rw > 0.001f)
            {
                anim.SetIKPosition(AvatarIKGoal.RightFoot, Vector3.Lerp(rf, rig.rightFootTarget, rig.rightFootWeight / Mathf.Max(rw, 1e-4f)));
                anim.SetIKRotation(AvatarIKGoal.RightFoot, rfr);
                if (rig.rightFootWeight > 0.01f) { anim.SetIKHintPositionWeight(AvatarIKHint.RightKnee, rig.rightFootWeight); anim.SetIKHintPosition(AvatarIKHint.RightKnee, rig.rightKneeHint); }
                else anim.SetIKHintPositionWeight(AvatarIKHint.RightKnee, 0f);
            }

            // weapon hand
            if (rig.weapon != null)
            {
                anim.SetIKPositionWeight(AvatarIKGoal.RightHand, rig.rightHandWeight);
                anim.SetIKPosition(AvatarIKGoal.RightHand, rig.GripWorld);
            }
            else anim.SetIKPositionWeight(AvatarIKGoal.RightHand, 0f);

            // torso: swing twist + hit reaction
            float twist = rig.torsoTwist + rig.ReactTwist, bend = rig.torsoBend - rig.ReactPitch;
            if (haveAnim && (Mathf.Abs(twist) > 0.05f || Mathf.Abs(bend) > 0.05f))
            {
                if (spine) anim.SetBoneLocalRotation(HumanBodyBones.Spine, spineAnim * Quaternion.AngleAxis(twist * 0.4f, spineUp) * Quaternion.AngleAxis(bend * 0.5f, spineRight));
                if (chest) anim.SetBoneLocalRotation(HumanBodyBones.Chest, chestAnim * Quaternion.AngleAxis(twist * 0.6f, chestUp) * Quaternion.AngleAxis(bend * 0.5f, chestRight));
            }

            // off hand: an aim (blaster / tether) beats the two-handed grip or the shield handle
            if (rig.leftHandAimWeight > 0.001f)
            {
                anim.SetIKPositionWeight(AvatarIKGoal.LeftHand, rig.leftHandAimWeight);
                anim.SetIKPosition(AvatarIKGoal.LeftHand, rig.leftHandAim);
            }
            else
            {
                anim.SetIKPositionWeight(AvatarIKGoal.LeftHand, rig.weapon != null || rig.leftHandTarget ? rig.leftHandWeight : 0f);
                if (rig.leftHandWeight > 0.001f && (rig.weapon != null || rig.leftHandTarget))
                {
                    float s = rig.transform.lossyScale.y;
                    anim.SetIKPosition(AvatarIKGoal.LeftHand, rig.leftHandTarget ? rig.leftHandTarget.position : rig.GripWorld - rig.BladeDirWorld * (0.14f * s));
                }
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
