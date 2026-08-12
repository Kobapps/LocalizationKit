using System;
using UnityEngine;

namespace LocalizationKit
{
    /// <summary>
    /// The registry of live localized objects, and the thing that pushes a language change out to
    /// all of them.
    /// </summary>
    /// <remarks>
    /// This exists instead of a plain C# event for three reasons, all of which show up at scale:
    /// <list type="number">
    /// <item><b>Registration does not allocate.</b> Subscribing a method to an event allocates a
    /// delegate — one per object, per subscribe. A scene with a few thousand localized labels
    /// would allocate on every load.</item>
    /// <item><b>Unregistering is O(1).</b> A subscription carries its slot, so tear-down is a
    /// write, not a scan of the invocation list.</item>
    /// <item><b>One bad object cannot silence the rest.</b> A throwing handler in a multicast
    /// delegate aborts the whole invocation; here it is caught, logged against the object that
    /// threw, and the loop carries on.</item>
    /// </list>
    /// Slots are handed out from a free list and reused, so a scene that repeatedly spawns and
    /// destroys localized objects reaches a steady state and stops growing.
    /// </remarks>
    public static class LocalizationBinder
    {
        // Slot 0 is never handed out, so a default(LocalizationSubscription) is inactive.
        private static ILocalizedObject[] s_Objects = new ILocalizedObject[64];
        private static int[] s_FreeSlots = new int[16];
        private static int s_FreeCount;
        private static int s_HighWater = 1;
        private static bool s_Applying;

        /// <summary>Number of live registrations.</summary>
        public static int Count
        {
            get
            {
                var live = 0;
                for (var i = 1; i < s_HighWater; i++)
                    if (s_Objects[i] != null) live++;

                return live;
            }
        }

        /// <summary>
        /// Registers an object and applies current text to it immediately, so a freshly spawned
        /// object is never briefly wrong.
        /// </summary>
        public static LocalizationSubscription Register(ILocalizedObject target)
        {
            if (target == null) return default;

            int slot;
            if (s_FreeCount > 0)
            {
                slot = s_FreeSlots[--s_FreeCount];
            }
            else
            {
                slot = s_HighWater++;
                if (slot >= s_Objects.Length)
                    Array.Resize(ref s_Objects, s_Objects.Length * 2);
            }

            s_Objects[slot] = target;

            Apply(target);

            return new LocalizationSubscription(slot);
        }

        /// <summary>
        /// Releases a registration and clears the subscription, so a double call is harmless.
        /// </summary>
        public static void Unregister(ref LocalizationSubscription subscription)
        {
            var slot = subscription.m_Slot;
            if (slot <= 0 || slot >= s_HighWater) return;

            s_Objects[slot] = null;
            subscription.m_Slot = 0;

            if (s_FreeCount == s_FreeSlots.Length)
                Array.Resize(ref s_FreeSlots, s_FreeSlots.Length * 2);

            s_FreeSlots[s_FreeCount++] = slot;
        }

        /// <summary>
        /// Refreshes every registered object. Called by <see cref="Localization"/> on a language
        /// change or a catalog reload; safe to call directly after changing something the shipped
        /// components read.
        /// </summary>
        public static void ApplyAll()
        {
            // Registering from inside ApplyLocalization would append to the array while we walk it.
            // The guard keeps that from recursing; the new object was already applied on Register.
            if (s_Applying) return;

            s_Applying = true;
            try
            {
                var high = s_HighWater;
                for (var i = 1; i < high; i++)
                {
                    var target = s_Objects[i];
                    if (target != null) Apply(target);
                }
            }
            finally
            {
                s_Applying = false;
            }
        }

        private static void Apply(ILocalizedObject target)
        {
            try
            {
                target.ApplyLocalization();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, target as UnityEngine.Object);
            }
        }

        /// <summary>
        /// Drops every registration. For tests and for domain-reload-disabled play mode, where
        /// statics survive a play session and would otherwise hold dead objects.
        /// </summary>
        public static void Clear()
        {
            Array.Clear(s_Objects, 0, s_Objects.Length);
            s_FreeCount = 0;
            s_HighWater = 1;
            s_Applying = false;
        }
    }
}
