using System;
using System.Reflection;

namespace ZG2
{
    /// <summary>
    /// Helper for subscribing to / unsubscribing from events that are ambiguous
    /// due to Krafs.Publicizer exposing both the event and its backing delegate field.
    /// Uses reflection to access the event's add/remove accessors directly.
    /// </summary>
    internal static class EventHelper
    {
        /// <summary>
        /// Subscribe a delegate to a static event via reflection.
        /// </summary>
        /// <param name="declaringType">The type that declares the event.</param>
        /// <param name="eventName">Name of the event.</param>
        /// <param name="handler">The delegate to subscribe.</param>
        public static void SubscribeStatic(Type declaringType, string eventName, Delegate handler)
        {
            var eventInfo = declaringType.GetEvent(eventName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

            if (eventInfo != null)
            {
                eventInfo.AddEventHandler(null, handler);
                return;
            }

            // Fallback: directly get the backing field and combine delegates
            var field = declaringType.GetField(eventName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

            if (field != null)
            {
                var current = (Delegate)field.GetValue(null);
                field.SetValue(null, Delegate.Combine(current, handler));
            }
            else
            {
                Main.LogError($"EventHelper: could not find event or field '{eventName}' on {declaringType.Name}");
            }
        }

        /// <summary>
        /// Unsubscribe a delegate from a static event via reflection.
        /// </summary>
        public static void UnsubscribeStatic(Type declaringType, string eventName, Delegate handler)
        {
            var eventInfo = declaringType.GetEvent(eventName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

            if (eventInfo != null)
            {
                eventInfo.RemoveEventHandler(null, handler);
                return;
            }

            var field = declaringType.GetField(eventName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

            if (field != null)
            {
                var current = (Delegate)field.GetValue(null);
                field.SetValue(null, Delegate.Remove(current, handler));
            }
        }

        /// <summary>
        /// Subscribe a delegate to an instance event via reflection.
        /// </summary>
        /// <param name="target">The object instance.</param>
        /// <param name="eventName">Name of the event.</param>
        /// <param name="handler">The delegate to subscribe.</param>
        public static void Subscribe(object target, string eventName, Delegate handler)
        {
            var type = target.GetType();
            var eventInfo = type.GetEvent(eventName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            if (eventInfo != null)
            {
                eventInfo.AddEventHandler(target, handler);
                return;
            }

            var field = type.GetField(eventName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            if (field != null)
            {
                var current = (Delegate)field.GetValue(target);
                field.SetValue(target, Delegate.Combine(current, handler));
            }
            else
            {
                Main.LogError($"EventHelper: could not find event or field '{eventName}' on {type.Name}");
            }
        }

        /// <summary>
        /// Unsubscribe a delegate from an instance event via reflection.
        /// </summary>
        public static void Unsubscribe(object target, string eventName, Delegate handler)
        {
            var type = target.GetType();
            var eventInfo = type.GetEvent(eventName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            if (eventInfo != null)
            {
                eventInfo.RemoveEventHandler(target, handler);
                return;
            }

            var field = type.GetField(eventName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            if (field != null)
            {
                var current = (Delegate)field.GetValue(target);
                field.SetValue(target, Delegate.Remove(current, handler));
            }
        }
    }
}
