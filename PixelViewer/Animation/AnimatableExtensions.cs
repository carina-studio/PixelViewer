using Avalonia.Animation;
using System;
using System.Runtime.CompilerServices;

namespace Carina.PixelViewer.Animation
{
    /// <summary>
    /// Extensions for <see cref="Animatable"/>.
    /// </summary>
    static class AnimatableExtensions
    {
        /// <summary>
        /// Disable transitions temporarily and perform action.
        /// </summary>
        /// <param name="animatable"><see cref="Animatable"/>.</param>
        /// <param name="action">Action.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void DisableTransitionsAndRun(this Animatable animatable, Action action)
        {
            var transitions = animatable.Transitions;
            if (transitions == null)
                action();
            else
            {
                animatable.Transitions = null;
                try
                {
                    action();
                }
                finally
                {
                    animatable.Transitions = transitions;
                }
            }
        }
    }
}
