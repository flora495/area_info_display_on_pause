using EntityComponent;
using JumpKing.Player;

namespace AreaInfoDisplayOnPause
{
    /// <summary>
    /// Mirrors the engine's own LocationComp.IsPlayerOnGround() (used to gate its "new area
    /// discovered" popup): area progress should only register once the player has actually
    /// landed somewhere in that area, not merely passed through it mid-air.
    /// </summary>
    internal static class PlayerGroundChecker
    {
        public static bool IsOnGround()
        {
            PlayerEntity player = EntityManager.instance?.Find<PlayerEntity>();
            return player?.GetComponent<BodyComp>()?.IsOnGround ?? false;
        }
    }
}
