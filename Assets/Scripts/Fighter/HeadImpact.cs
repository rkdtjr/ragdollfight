using UnityEngine;

namespace RagdollFight
{
    /// <summary>머리에 붙는다. 상대 몸이 세게 부딪히면 기절시킨다.</summary>
    public class HeadImpact : MonoBehaviour
    {
        public FighterController owner;

        void OnCollisionEnter(Collision c)
        {
            if (c.rigidbody == null || owner.Owns(c.rigidbody)) return;
            if (c.relativeVelocity.magnitude >= owner.koImpactSpeed) owner.KnockOut();
        }
    }
}
