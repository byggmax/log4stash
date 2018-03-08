using log4net.Core;

namespace BMX.Infra.log4stash.Extensions
{
    public static class FixFlagsExtensions
    {
        public static bool ContainsFlag(this FixFlags flagsEnum, FixFlags flag)
        {
            return (flagsEnum & flag) != 0;
        }
    }
}