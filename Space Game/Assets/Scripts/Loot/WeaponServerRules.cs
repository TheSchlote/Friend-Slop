namespace FriendSlop.Loot
{
    public static class WeaponServerRules
    {
        public static bool CanLaserFire(
            bool shooterExists,
            bool shooterIsDead,
            bool isHeldByShooter,
            bool roundIsActive,
            int ammo,
            float serverTime,
            float nextAllowedFireTime)
        {
            return shooterExists
                   && !shooterIsDead
                   && isHeldByShooter
                   && roundIsActive
                   && ammo > 0
                   && serverTime >= nextAllowedFireTime;
        }

        public static bool CanBoxingGlovesPunch(
            bool attackerExists,
            bool attackerIsDead,
            bool isHeldByAttacker,
            bool roundIsActive,
            float serverTime,
            float nextAllowedPunchTime)
        {
            return attackerExists
                   && !attackerIsDead
                   && isHeldByAttacker
                   && roundIsActive
                   && serverTime >= nextAllowedPunchTime;
        }
    }
}
