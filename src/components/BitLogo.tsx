import React from 'react';

interface BitLogoProps {
  variant?: 'light' | 'dark' | 'auto'; // 'light' = for light bg (cyan text/boxes), 'dark' = for dark bg (white text/boxes)
  className?: string;
  height?: number | string;
  showText?: boolean;
}

export const BitLogo: React.FC<BitLogoProps> = ({
  variant = 'light',
  className = '',
  height = 40,
  showText = true,
}) => {
  // Color configuration based on theme variant
  const isDark = variant === 'dark';
  const boxStroke = isDark ? '#FFFFFF' : '#38A5D8';
  const boxOpacity = isDark ? 0.35 : 0.9;
  const textColor = isDark ? '#FFFFFF' : '#38A5D8';

  // Calculate width based on whether text is shown
  const viewBoxWidth = showText ? 390 : 196;

  return (
    <svg
      viewBox={`0 0 ${viewBoxWidth} 100`}
      height={height}
      className={`select-none ${className}`}
      fill="none"
      xmlns="http://www.w3.org/2000/svg"
      role="img"
      aria-label="Brand Inserts Technology (BIT) Logo"
    >
      {/* ==================== B EMBLEM ==================== */}
      <g id="b-emblem">
        {/* Top-Left Quadrant - Deep Violet */}
        <rect x="0" y="0" width="23" height="33.3" fill="#2E217B" />

        {/* Top Right Curved Lobe - Sky Blue */}
        <path
          d="M 23 0 L 43 0 C 55.7 0 66 10.3 66 23 C 66 35.7 55.7 46 43 46 L 23 46 Z"
          fill="#38A5D8"
        />

        {/* Overlap Center Region - Deep Purple */}
        <rect x="0" y="33.3" width="23" height="33.4" fill="#483082" />

        {/* Middle Right Curved Overlap Zone - Purple Magenta */}
        <path
          d="M 23 33.3 L 43 33.3 C 50 33.3 56 37 59.5 42.5 C 56 48 50 51.7 43 51.7 L 23 51.7 Z"
          fill="#6B2F8A"
        />

        {/* Bottom-Left Quadrant - Crimson Red */}
        <rect x="0" y="66.7" width="23" height="33.3" fill="#B81D4A" />

        {/* Bottom Right Curved Lobe - Pink/Magenta */}
        <path
          d="M 23 54 L 43 54 C 55.7 54 66 64.3 66 77 C 66 89.7 55.7 100 43 100 L 23 100 Z"
          fill="#D9418E"
        />
      </g>

      {/* ==================== 'I' GRID (3 Stacked Squares) ==================== */}
      <g id="i-grid" stroke={boxStroke} strokeWidth="2.5" strokeOpacity={boxOpacity}>
        <rect x="74" y="8" width="25" height="25" fill="none" />
        <rect x="74" y="37.5" width="25" height="25" fill="none" />
        <rect x="74" y="67" width="25" height="25" fill="none" />
      </g>

      {/* ==================== 'T' GRID (5 Squares T-Shape) ==================== */}
      <g id="t-grid" stroke={boxStroke} strokeWidth="2.5" strokeOpacity={boxOpacity}>
        {/* Top Row of T */}
        <rect x="105" y="8" width="25" height="25" fill="none" />
        <rect x="134.5" y="8" width="25" height="25" fill="none" />
        <rect x="164" y="8" width="25" height="25" fill="none" />
        {/* Vertical Stem of T */}
        <rect x="134.5" y="37.5" width="25" height="25" fill="none" />
        <rect x="134.5" y="67" width="25" height="25" fill="none" />
      </g>

      {/* ==================== BRAND INSERTS TECHNOLOGY TEXT ==================== */}
      {showText && (
        <g id="brand-text" fill={textColor}>
          <text
            x="202"
            y="31"
            fontFamily="system-ui, -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif"
            fontWeight="800"
            fontSize="24"
            letterSpacing="0.5"
          >
            BRAND
          </text>
          <text
            x="202"
            y="60.5"
            fontFamily="system-ui, -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif"
            fontWeight="800"
            fontSize="24"
            letterSpacing="0.5"
          >
            INSERTS
          </text>
          <text
            x="202"
            y="90"
            fontFamily="system-ui, -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif"
            fontWeight="800"
            fontSize="24"
            letterSpacing="0.5"
          >
            TECHNOLOGY
          </text>
        </g>
      )}
    </svg>
  );
};
