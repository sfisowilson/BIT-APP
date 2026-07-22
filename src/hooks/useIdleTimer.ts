import { useEffect, useRef, useState, useCallback } from 'react';

interface UseIdleTimerOptions {
  /** Minutes of inactivity before showing countdown warning. Default 28. */
  idleMinutes?: number;
  /** Seconds the warning countdown lasts before auto-logout. Default 60. */
  countdownSeconds?: number;
  /** Called when timeout expires and user should be logged out. */
  onTimeout: () => void;
}

interface UseIdleTimerReturn {
  /** True when the countdown warning modal should be shown. */
  showCountdown: boolean;
  /** Seconds remaining before auto-logout. */
  secondsRemaining: number;
  /** Call to reset the timer (e.g. after user confirms they're still there). */
  resetTimer: () => void;
}

const EVENTS: (keyof WindowEventMap)[] = [
  'mousemove',
  'keydown',
  'click',
  'scroll',
  'touchstart',
];

/**
 * MReq 8: Detects user inactivity and triggers a countdown warning
 * before auto-logout. Resets on any mouse/keyboard/touch activity.
 */
export function useIdleTimer({
  idleMinutes = 28,
  countdownSeconds = 60,
  onTimeout,
}: UseIdleTimerOptions): UseIdleTimerReturn {
  const [showCountdown, setShowCountdown] = useState(false);
  const [secondsRemaining, setSecondsRemaining] = useState(countdownSeconds);
  const timeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const countdownRef = useRef<ReturnType<typeof setInterval> | null>(null);
  const onTimeoutRef = useRef(onTimeout);
  onTimeoutRef.current = onTimeout;

  const clearAll = useCallback(() => {
    if (timeoutRef.current) { clearTimeout(timeoutRef.current); timeoutRef.current = null; }
    if (countdownRef.current) { clearInterval(countdownRef.current); countdownRef.current = null; }
  }, []);

  const startIdleTimer = useCallback(() => {
    clearAll();
    setShowCountdown(false);
    setSecondsRemaining(countdownSeconds);

    timeoutRef.current = setTimeout(() => {
      // Idle threshold reached — show countdown
      setShowCountdown(true);
      let remaining = countdownSeconds;
      setSecondsRemaining(remaining);

      countdownRef.current = setInterval(() => {
        remaining--;
        setSecondsRemaining(remaining);
        if (remaining <= 0) {
          clearAll();
          onTimeoutRef.current();
        }
      }, 1000);
    }, idleMinutes * 60 * 1000);
  }, [idleMinutes, countdownSeconds, clearAll]);

  const resetTimer = useCallback(() => {
    startIdleTimer();
  }, [startIdleTimer]);

  useEffect(() => {
    startIdleTimer();

    const handleActivity = () => startIdleTimer();
    EVENTS.forEach(event => window.addEventListener(event, handleActivity, { passive: true }));

    return () => {
      clearAll();
      EVENTS.forEach(event => window.removeEventListener(event, handleActivity));
    };
  }, [startIdleTimer, clearAll]);

  return { showCountdown, secondsRemaining, resetTimer };
}
