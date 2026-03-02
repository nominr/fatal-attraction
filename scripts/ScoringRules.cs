using System;
using System.Numerics;

namespace FatalAttraction.Engine
{
    public static class ScoringRules
    {
        // Vector Format: [Admirer, Prophet, Producer]
        // States always sum to 3. Modification vectors always sum to 0.
        // No component ever goes negative.

        public const float StateSum = 3f;
        public const float CaughtPenalty = 0.6f;

        // --- Win vectors (chat game win, punch alone NPC) ---
        // Winner gets +1, each loser gets -0.5  => sums to 0

        public static Vector3 GetWinPoints(Role winner)
        {
            return winner switch
            {
                Role.Admirer  => new Vector3( 1f, -0.5f, -0.5f),
                Role.Prophet  => new Vector3(-0.5f,  1f, -0.5f),
                Role.Producer => new Vector3(-0.5f, -0.5f,  1f),
                _             => Vector3.Zero
            };
        }

        // --- Loss vectors (chat game loss) ---
        // Loser gets -1, each other gets +0.5  => sums to 0
        public static Vector3 GetLossPoints(Role loser)
        {
            return -GetWinPoints(loser);
        }

        // --- Punch interacting: both involved get -1, third gets +2 => sums to 0 ---
        public static Vector3 GetPunchInteractingPoints(Role puncher, Role conversing)
        {
            // Third role gets +2
            var third = GetThirdRole(puncher, conversing);
            return third switch
            {
                Role.Admirer  => new Vector3( 2f, -1f, -1f),
                Role.Prophet  => new Vector3(-1f,  2f, -1f),
                Role.Producer => new Vector3(-1f, -1f,  2f),
                _             => Vector3.Zero
            };
        }

        public static Role GetThirdRole(Role a, Role b)
        {
            foreach (Role r in Enum.GetValues(typeof(Role)))
            {
                if (r != a && r != b) return r;
            }
            return a; // fallback, should never happen
        }

        // --- Legacy compatibility wrappers (return the win vector) ---
        public static Vector3 GetFlirtPoints(int score)
        {
            if (score > 0) return GetWinPoints(Role.Admirer);
            if (score < 0) return GetLossPoints(Role.Admirer);
            return Vector3.Zero;
        }

        public static Vector3 GetConversionPoints(int score)
        {
            if (score > 0) return GetWinPoints(Role.Prophet);
            if (score < 0) return GetLossPoints(Role.Prophet);
            return Vector3.Zero;
        }

        public static Vector3 GetMoneyGamePoints(int score)
        {
            if (score > 0) return GetWinPoints(Role.Producer);
            if (score < 0) return GetLossPoints(Role.Producer);
            return Vector3.Zero;
        }

        // --- Normalize so components sum to StateSum (3), no negatives ---
        public static Vector3 NormalizeState(Vector3 state)
        {
            float x = Math.Max(0, state.X);
            float y = Math.Max(0, state.Y);
            float z = Math.Max(0, state.Z);

            float sum = x + y + z;
            if (sum > 0.0001f)
            {
                float scale = StateSum / sum;
                return new Vector3(x * scale, y * scale, z * scale);
            }
            else
            {
                return new Vector3(1f, 1f, 1f); // center
            }
        }

        // --- Clamp state: enforce no-negatives, then re-normalize to sum to 3 ---
        public static Vector3 ClampState(Vector3 state)
        {
            return NormalizeState(state);
        }
    }
}
