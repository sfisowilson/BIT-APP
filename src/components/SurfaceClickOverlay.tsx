import React from 'react';
import { previewSegment } from '../apiClient';
import { parseMaskPolygon, type MaskPolygon } from '../types';

export type InteractionMode = 'product' | 'signage';

export interface QuadPoint {
  x: number;  // native video pixel coordinates
  y: number;
}

export interface SurfaceClickOverlayProps {
  videoRef: React.RefObject<HTMLVideoElement | null>;
  contentId: string;
  currentFrame: number;
  frameRate: number;
  mode: InteractionMode;
  assetUrl?: string;         // For live warp preview in signage mode
  onMaskReceived?: (polygon: MaskPolygon) => void;
  onQuadConfirmed?: (corners: [QuadPoint, QuadPoint, QuadPoint, QuadPoint]) => void;
  onCancel?: () => void;
}

/**
 * Handles click-to-segment (Insert Product) and draw-to-place (Place Signage)
 * interactions on the video player in the Placement Editor.
 *
 * Insert Product: click → SAM3 preview → SVG polygon overlay
 * Place Signage: click 4 corners → draggable quad → live warp preview
 */
export const SurfaceClickOverlay: React.FC<SurfaceClickOverlayProps> = ({
  videoRef,
  contentId,
  currentFrame,
  frameRate,
  mode,
  assetUrl,
  onMaskReceived,
  onQuadConfirmed,
  onCancel,
}) => {
  const [loading, setLoading] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);

  // Product mode: mask polygon from SAM3
  const [maskPolygon, setMaskPolygon] = React.useState<MaskPolygon | null>(null);

  // Signage mode: user-placed quad corners (in native video coords)
  const [quadCorners, setQuadCorners] = React.useState<QuadPoint[]>([]);
  const [draggingCorner, setDraggingCorner] = React.useState<number | null>(null);
  const [previewImage, setPreviewImage] = React.useState<string | null>(null);

  // Clear state when mode changes
  React.useEffect(() => {
    setMaskPolygon(null);
    setQuadCorners([]);
    setError(null);
    setPreviewImage(null);
  }, [mode]);

  // ── Coordinate scaling: CSS pixels → native video pixels ──
  const cssToNative = (cssX: number, cssY: number): QuadPoint => {
    const vid = videoRef.current;
    if (!vid) return { x: 0, y: 0 };
    const scaleX = vid.videoWidth / vid.clientWidth;
    const scaleY = vid.videoHeight / vid.clientHeight;
    return {
      x: Math.round(cssX * scaleX),
      y: Math.round(cssY * scaleY),
    };
  };

  const nativeToCss = (natX: number, natY: number): { x: number; y: number } => {
    const vid = videoRef.current;
    if (!vid) return { x: 0, y: 0 };
    const scaleX = vid.clientWidth / vid.videoWidth;
    const scaleY = vid.clientHeight / vid.videoHeight;
    return { x: natX * scaleX, y: natY * scaleY };
  };

  // ── Product Mode: Click handler ──
  const handleProductClick = async (e: React.MouseEvent<HTMLDivElement>) => {
    if (mode !== 'product' || loading) return;

    const vid = videoRef.current;
    if (!vid) return;

    const rect = vid.getBoundingClientRect();
    const cssX = e.clientX - rect.left;
    const cssY = e.clientY - rect.top;

    // Ensure click is within the rendered video area (not letterbox bars)
    if (cssX < 0 || cssY < 0 || cssX > rect.width || cssY > rect.height) return;

    const native = cssToNative(cssX, cssY);
    if (native.x >= vid.videoWidth || native.y >= vid.videoHeight) return;

    setLoading(true);
    setError(null);

    try {
      const result = await previewSegment({
        contentId,
        frameIndex: currentFrame,
        x: native.x,
        y: native.y,
      });

      if (!result.maskPolygonJson || result.maskPolygonJson === '[]') {
        setError(result.surfaceType || 'No distinct surface found.');
        return;
      }

      const polygon = parseMaskPolygon(result);
      setMaskPolygon(polygon);
      onMaskReceived?.(polygon);
    } catch (err: any) {
      setError(err.message || 'Preview failed.');
    } finally {
      setLoading(false);
    }
  };

  // ── Signage Mode: Quad placement ──
  const handleSignageClick = (e: React.MouseEvent<HTMLDivElement>) => {
    if (mode !== 'signage' || loading) return;
    if (quadCorners.length >= 4) return; // All 4 corners placed

    const vid = videoRef.current;
    if (!vid) return;

    const rect = vid.getBoundingClientRect();
    const cssX = e.clientX - rect.left;
    const cssY = e.clientY - rect.top;

    const native = cssToNative(cssX, cssY);
    const newCorners = [...quadCorners, native];
    setQuadCorners(newCorners);

    // On 4th corner → confirm
    if (newCorners.length === 4) {
      onQuadConfirmed?.(newCorners as [QuadPoint, QuadPoint, QuadPoint, QuadPoint]);
    }
  };

  // ── Drag corner handler ──
  const handleCornerMouseDown = (index: number) => (e: React.MouseEvent) => {
    e.stopPropagation();
    setDraggingCorner(index);
  };

  React.useEffect(() => {
    if (draggingCorner === null) return;

    const vid = videoRef.current;
    if (!vid) return;

    const handleMove = (e: MouseEvent) => {
      const rect = vid.getBoundingClientRect();
      const cssX = e.clientX - rect.left;
      const cssY = e.clientY - rect.top;
      const native = cssToNative(cssX, cssY);
      setQuadCorners(prev => {
        const updated = [...prev];
        updated[draggingCorner!] = native;
        return updated;
      });
    };

    const handleUp = () => setDraggingCorner(null);

    window.addEventListener('mousemove', handleMove);
    window.addEventListener('mouseup', handleUp);
    return () => {
      window.removeEventListener('mousemove', handleMove);
      window.removeEventListener('mouseup', handleUp);
    };
  }, [draggingCorner]);

  // ── Live warp preview (Canvas) ──
  React.useEffect(() => {
    if (mode !== 'signage' || quadCorners.length < 4 || !assetUrl) {
      setPreviewImage(null);
      return;
    }

    const canvas = document.createElement('canvas');
    const vid = videoRef.current;
    if (!vid) return;
    canvas.width = vid.clientWidth;
    canvas.height = vid.clientHeight;
    const ctx = canvas.getContext('2d');
    if (!ctx) return;

    const img = new Image();
    img.crossOrigin = 'anonymous';
    img.onload = () => {
      // Map asset corners (0,0)→(imgW,0)→(imgW,imgH)→(0,imgH) to quad in CSS coords
      const src = [0, 0, img.width, 0, img.width, img.height, 0, img.height];
      const dst = quadCorners.flatMap(c => {
        const css = nativeToCss(c.x, c.y);
        return [css.x, css.y];
      });

      // Simple perspective transform via Canvas 2D — approximate with drawImage + clip
      ctx.clearRect(0, 0, canvas.width, canvas.height);
      ctx.save();
      ctx.beginPath();
      ctx.moveTo(dst[0], dst[1]);
      ctx.lineTo(dst[2], dst[3]);
      ctx.lineTo(dst[4], dst[5]);
      ctx.lineTo(dst[6], dst[7]);
      ctx.closePath();
      ctx.clip();
      ctx.drawImage(img, 0, 0, canvas.width, canvas.height);
      ctx.restore();

      setPreviewImage(canvas.toDataURL());
    };
    img.src = assetUrl;
  }, [quadCorners, assetUrl, mode]);

  // ── Render ──
  return (
    <div
      style={{
        position: 'absolute',
        top: 0,
        left: 0,
        width: '100%',
        height: '100%',
        cursor: mode === 'product' ? 'crosshair' : quadCorners.length < 4 ? 'crosshair' : 'default',
        zIndex: 10,
      }}
      onClick={mode === 'product' ? handleProductClick : handleSignageClick}
    >
      {/* Loading overlay */}
      {loading && (
        <div style={{
          position: 'absolute', inset: 0,
          background: 'rgba(0,0,0,0.3)',
          display: 'flex', alignItems: 'center', justifyContent: 'center',
          zIndex: 20,
        }}>
          <div style={{ color: '#fff', fontSize: 14 }}>
            Segmenting with SAM3…
          </div>
        </div>
      )}

      {/* Error message */}
      {error && (
        <div style={{
          position: 'absolute', top: 12, left: '50%', transform: 'translateX(-50%)',
          background: 'rgba(239,68,68,0.9)', color: '#fff',
          padding: '8px 16px', borderRadius: 8, fontSize: 13, zIndex: 20,
        }}>
          {error}
          <button
            onClick={(e) => { e.stopPropagation(); setError(null); }}
            style={{ marginLeft: 12, background: 'none', border: 'none', color: '#fff', cursor: 'pointer', fontWeight: 'bold' }}
          >
            ×
          </button>
        </div>
      )}

      {/* SVG overlay for mask or quad */}
      <svg
        style={{
          position: 'absolute', top: 0, left: 0,
          width: '100%', height: '100%',
          pointerEvents: 'none',
        }}
        viewBox={`0 0 ${videoRef.current?.videoWidth || 1920} ${videoRef.current?.videoHeight || 1080}`}
        preserveAspectRatio="xMidYMid meet"
      >
        {/* Product mode: mask polygon */}
        {mode === 'product' && maskPolygon && maskPolygon.points.length >= 3 && (
          <g>
            <polygon
              points={maskPolygon.points.map(p => `${p.x},${p.y}`).join(' ')}
              fill="rgba(59,130,246,0.3)"
              stroke="#3B82F6"
              strokeWidth="3"
              strokeDasharray=""
              filter="url(#glow)"
            />
            {/* Glow filter */}
            <defs>
              <filter id="glow" x="-20%" y="-20%" width="140%" height="140%">
                <feGaussianBlur stdDeviation="3" result="blur" />
                <feComposite in="SourceGraphic" in2="blur" operator="over" />
              </filter>
            </defs>
          </g>
        )}

        {/* Signage mode: quad with corners */}
        {mode === 'signage' && quadCorners.length > 0 && (
          <g>
            {/* Quad edges */}
            {quadCorners.length >= 2 && (
              <polyline
                points={quadCorners.map(c => `${c.x},${c.y}`).join(' ')}
                fill="none"
                stroke={quadCorners.length === 4 ? '#10B981' : '#F59E0B'}
                strokeWidth="2.5"
                strokeDasharray={quadCorners.length < 4 ? '8 4' : '0'}
              />
            )}
            {/* Close the quad if 4 corners */}
            {quadCorners.length === 4 && (
              <line
                x1={quadCorners[3].x} y1={quadCorners[3].y}
                x2={quadCorners[0].x} y2={quadCorners[0].y}
                stroke="#10B981" strokeWidth="2.5"
              />
            )}
            {/* Corner handles */}
            {quadCorners.map((corner, i) => (
              <g key={i}>
                <circle
                  cx={corner.x} cy={corner.y} r="8"
                  fill={i === draggingCorner ? '#10B981' : '#fff'}
                  stroke="#10B981" strokeWidth="2"
                  style={{ cursor: 'grab', pointerEvents: 'all' }}
                  onMouseDown={handleCornerMouseDown(i)}
                />
                <text
                  x={corner.x + 14} y={corner.y - 6}
                  fill="#10B981" fontSize="14" fontWeight="bold"
                  style={{ pointerEvents: 'none', userSelect: 'none' }}
                >
                  {i + 1}
                </text>
              </g>
            ))}
          </g>
        )}
      </svg>

      {/* Live warp preview (signage mode, after 4 corners) */}
      {mode === 'signage' && previewImage && (
        <img
          src={previewImage}
          alt="Warp preview"
          style={{
            position: 'absolute', top: 0, left: 0,
            width: '100%', height: '100%',
            opacity: 0.7,
            pointerEvents: 'none',
          }}
        />
      )}
    </div>
  );
};

export default SurfaceClickOverlay;
