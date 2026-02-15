using System;
using System.Numerics;

namespace FatalAttraction.Engine
{
    public static class ScoringRules
    {
        // Vector Format: [Admirer, Prophet, Producer]
        
        public const float CaughtPenalty = 0.6f;

        public static Vector3 GetFlirtPoints(int score)
        {
            // Admirer: Flirting generates points from -2 to 2
            return new Vector3(score, 0, 0);
        }

        public static Vector3 GetConversionPoints(int score)
        {
            // Prophet: Conversion generates points from -2 to 2
            // Score based on 2-round RPS (Win=+1, Loss=-1, Tie=0)
            return new Vector3(0, score, 0);
        }

        public static Vector3 GetMoneyGamePoints(int score)
        {
            // Producer: Money game generates +1 (Success) or -1 (Failure)
            return new Vector3(0, 0, score);
        }

        public static Vector3 NormalizeState(Vector3 state)
        {
            // Clamp negative values to 0 for normalization purposes (probability distribution)
            float x = Math.Max(0, state.X);
            float y = Math.Max(0, state.Y);
            float z = Math.Max(0, state.Z);
            
            float sum = x + y + z;
            if (sum > 0.0001f)
            {
                return new Vector3(x / sum, y / sum, z / sum);
            }
            else
            {
                // Default to even split if sum is 0 or negative
                return new Vector3(0.33f, 0.33f, 0.33f);
            }
        }
    }
}
