using System;
using UnityEngine;

namespace Krane.AdsCameraDistance
{
    // Error gate for a per-frame hook: after an exception the hook is skipped for a back-off (1 s, doubling up
    // to 60 s) and then retried, so a one-off error (e.g. during a scene change) does not disable the feature
    // for the session, while a persistent one is logged at most about once a minute. The first error is logged
    // with its stack trace, repeats as one line.
    internal sealed class Fault
    {
        private readonly string _what;
        private int _count;
        private float _until;

        public Fault(string what)
        {
            _what = what;
        }

        public bool Blocked => _count > 0 && Time.unscaledTime < _until;

        public void Hit(Exception e)
        {
            _count++;
            float delay = Mathf.Min(Mathf.Pow(2f, _count - 1), 60f);
            _until = Time.unscaledTime + delay;
            if (_count == 1)
                Plugin.Log.LogError($"{_what} failed, retrying in {delay:0} s: {e}");
            else
                Plugin.Log.LogError($"{_what} failed again ({_count}x), retrying in {delay:0} s: {e.GetType().Name}: {e.Message}");
        }

        public void Ok()
        {
            _count = 0;
        }
    }
}
