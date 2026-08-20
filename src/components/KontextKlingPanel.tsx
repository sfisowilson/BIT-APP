import React from 'react';
import { Loader2, Wand2, Play, RotateCcw, CheckCircle, Image, Video, ArrowRight, AlertTriangle, Upload, X, Maximize2 } from 'lucide-react';
import {
  SceneItem,
  SurfaceItem,
  CreativeAsset,
  RenderItem,
  CreateKontextFrameRequest,
  PropagateKlingRequest,
} from '../types';
import { submitKontextFrame, uploadKontextFrame, propagateKling, approvePromptSplice, suggestKontextPrompt } from '../apiClient';

interface KontextKlingPanelProps {
  currentScene: SceneItem | undefined;
  /** The currently-selected surface (may be undefined if none detected at the paused frame). */
  currentSurface?: SurfaceItem | undefined;
  campaignAssets: CreativeAsset[];
  contentId: string;
  campaignId?: string;
  /** Current frame number from the video player (user paused here). */
  currentFrame: number;
  /** The render currently being worked on for this scene (KontextStep or Kling propagation). */
  activeRender?: RenderItem | null;
  onRenderCreated: (render: RenderItem) => void;
  /** Marks/unmarks a Finished render as the one used for this scene in the final combined video. */
  onSetRenderQueuedForFinal?: (renderId: string, queued: boolean) => Promise<void>;
}

type Step = 'setup' | 'generating-kontext' | 'review-frame' | 'generating-kling' | 'review-video';

// Illustrative placeholder text only — NOT sent when the field is left blank. An earlier version
// of this made blank actually send this generic string, which overwrote the specific placement
// prompt from the Kontext step and caused Kling to add unrelated scene elements instead of
// staying focused on the brand. Leaving the field blank now correctly falls through to the
// backend's own default: keep reusing the original, specific placement prompt.
const DEFAULT_KLING_PROPAGATION_PROMPT =
  'Keep the brand placement consistent across the entire scene, matching all camera movements.';

export const KontextKlingPanel: React.FC<KontextKlingPanelProps> = ({
  currentScene,
  currentSurface,
  campaignAssets,
  contentId,
  campaignId,
  currentFrame,
  activeRender,
  onRenderCreated,
  onSetRenderQueuedForFinal,
}) => {
  const [assetId, setAssetId] = React.useState('');
  const [promptText, setPromptText] = React.useState('');
  const [frameNumber, setFrameNumber] = React.useState(currentFrame);
  const [submitting, setSubmitting] = React.useState(false);
  const [error, setError] = React.useState('');
  const [klingPrompt, setKlingPrompt] = React.useState('');
  const [suggesting, setSuggesting] = React.useState(false);
  const [suggestion, setSuggestion] = React.useState<{ original: string; suggested: string } | null>(null);
  const [provider, setProvider] = React.useState<'flux-kontext' | 'nano-banana-pro'>('flux-kontext');
  const [uploadingFrame, setUploadingFrame] = React.useState(false);
  const [queuing, setQueuing] = React.useState(false);
  const [fullscreenImage, setFullscreenImage] = React.useState<string | null>(null);
  const fileInputRef = React.useRef<HTMLInputElement>(null);
  // Quick mode subsumes what used to be the separate one-shot "Anchor & Generate" flow: skip
  // pausing for frame review and send straight to Kling once the Kontext frame is ready. Off by
  // default so existing step-by-step behavior (pause + review before propagating) is unchanged
  // unless the user explicitly opts in.
  const [quickMode, setQuickMode] = React.useState(false);
  // Guards against firing propagate more than once for the same render if this effect re-runs
  // (e.g. parent re-renders activeRender with the same id/status).
  const autoPropagatedRenderId = React.useRef<string | null>(null);

  // Close the full-screen preview on Escape.
  React.useEffect(() => {
    if (!fullscreenImage) return;
    const onKeyDown = (e: KeyboardEvent) => { if (e.key === 'Escape') setFullscreenImage(null); };
    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, [fullscreenImage]);

  const fullscreenModal = fullscreenImage ? (
    <div
      className="fixed inset-0 bg-black/90 z-50 flex items-center justify-center p-6 cursor-zoom-out"
      onClick={() => setFullscreenImage(null)}
    >
      <button
        type="button"
        onClick={() => setFullscreenImage(null)}
        className="absolute top-4 right-4 text-white/70 hover:text-white cursor-pointer"
        aria-label="Close full-screen preview"
      >
        <X className="h-7 w-7" />
      </button>
      <img
        src={fullscreenImage}
        alt="Full-screen frame preview"
        className="max-w-full max-h-full object-contain rounded-lg cursor-default"
        onClick={(e) => e.stopPropagation()}
      />
    </div>
  ) : null;

  // Track the current step
  const isKontextGenerating = activeRender?.renderStatus === 'Queued' || activeRender?.renderStatus === 'Processing';
  const isKontextReady = activeRender?.renderStatus === 'KontextReady';
  const isKlingGenerating = activeRender?.renderStatus === 'Queued' || activeRender?.renderStatus === 'Processing';
  const isPreviewReady = activeRender?.renderStatus === 'PreviewReady';
  const isFinished = activeRender?.renderStatus === 'Finished' || activeRender?.renderStatus === 'NeedsReview';
  const isFailed = activeRender?.renderStatus === 'Failed';

  // When the video player frame changes, update our frame number
  React.useEffect(() => {
    if (currentFrame > 0) setFrameNumber(currentFrame);
  }, [currentFrame]);

  // Clear error/suggestion when scene changes
  React.useEffect(() => {
    setError('');
    setSuggestion(null);
  }, [currentScene?.id, activeRender?.id]);

  const handleSuggestPrompt = async () => {
    if (!assetId || !promptText.trim()) {
      setError('Select an asset and write a rough placement idea first.');
      return;
    }
    setSuggesting(true);
    setError('');
    try {
      const result = await suggestKontextPrompt({
        contentId,
        frameNumber,
        assetId,
        surfaceId: currentSurface?.id,
        roughPrompt: promptText.trim(),
      });
      setSuggestion({ original: promptText.trim(), suggested: result.suggestedPrompt });
    } catch (err: any) {
      setError(err.message || 'Failed to get a suggestion from Gemini.');
    } finally {
      setSuggesting(false);
    }
  };

  if (!currentScene) {
    return <div className="text-xs text-slate-400 italic text-center py-6">Select a scene to start the Kontext→Kling workflow.</div>;
  }

  if (currentFrame <= 0) {
    return (
      <div className="text-[10px] text-amber-600 bg-amber-50 p-2.5 rounded-lg border border-amber-200">
        ⚠️ Pause the video at the frame where you want to anchor the placement. Current frame: {currentFrame}
      </div>
    );
  }

  const handleGenerateKontext = async () => {
    if (!assetId || !promptText.trim()) {
      setError('Select an asset and write a placement prompt.');
      return;
    }
    setSubmitting(true);
    setError('');
    try {
      const dto: CreateKontextFrameRequest = {
        contentId,
        sceneId: currentScene.id,
        surfaceId: currentSurface?.id,
        campaignId: campaignId || '',
        assetId,
        frameNumber,
        promptText: promptText.trim(),
        provider,
      };
      const render = await submitKontextFrame(dto);
      onRenderCreated(render);
    } catch (err: any) {
      setError(err.message || 'Failed to generate Kontext frame.');
    } finally {
      setSubmitting(false);
    }
  };

  const handleUploadFrame = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    e.target.value = ''; // allow re-selecting the same file later
    if (!file) return;
    if (!assetId) {
      setError('Select a brand asset first, then upload the reference frame.');
      return;
    }
    setUploadingFrame(true);
    setError('');
    try {
      const render = await uploadKontextFrame({
        contentId,
        sceneId: currentScene!.id,
        surfaceId: currentSurface?.id,
        campaignId: campaignId || '',
        assetId,
        frameNumber,
        promptText: promptText.trim() || undefined,
        file,
      });
      onRenderCreated(render);
    } catch (err: any) {
      setError(err.message || 'Failed to upload reference frame.');
    } finally {
      setUploadingFrame(false);
    }
  };

  const handlePropagateKling = async () => {
    if (!activeRender?.id) return;
    setSubmitting(true);
    setError('');
    try {
      // Leaving this blank must NOT send a generic filler prompt — the backend then keeps
      // reusing the original, specific placement prompt (e.g. "place the X logo on the Y
      // billboard face") set during the Kontext frame step, which is what actually keeps Kling
      // focused on just the brand. A hardcoded generic prompt was tried here and made Kling add
      // unrelated scene elements instead, because it overwrote that specific guidance.
      const dto: PropagateKlingRequest = {
        promptText: klingPrompt.trim() || undefined,
      };
      const render = await propagateKling(activeRender.id, dto);
      onRenderCreated(render);
    } catch (err: any) {
      setError(err.message || 'Failed to propagate with Kling.');
    } finally {
      setSubmitting(false);
    }
  };

  // Quick mode: once the Kontext frame lands, skip the review-frame pause and propagate
  // immediately — same net effect as the old one-shot "Anchor & Generate" flow.
  React.useEffect(() => {
    if (!quickMode) return;
    if (!isKontextReady || !activeRender) return;
    if (autoPropagatedRenderId.current === activeRender.id) return;
    autoPropagatedRenderId.current = activeRender.id;
    handlePropagateKling();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [quickMode, isKontextReady, activeRender?.id]);

  const handleQueue = async () => {
    if (!activeRender?.id || !onSetRenderQueuedForFinal) return;
    setQueuing(true);
    setError('');
    try {
      await onSetRenderQueuedForFinal(activeRender.id, !activeRender.isQueuedForFinal);
      onRenderCreated({ ...activeRender, isQueuedForFinal: !activeRender.isQueuedForFinal } as RenderItem);
    } catch (err: any) {
      setError(err.message || 'Failed to update queue status.');
    } finally {
      setQueuing(false);
    }
  };

  const handleApprove = async () => {
    if (!activeRender?.id) return;
    setSubmitting(true);
    setError('');
    try {
      await approvePromptSplice(activeRender.id);
      onRenderCreated({ ...activeRender, renderStatus: 'Processing', progress: 90 } as RenderItem);
    } catch (err: any) {
      setError(err.message || 'Failed to approve splice.');
    } finally {
      setSubmitting(false);
    }
  };

  // ── Step: Setup (choose frame, asset, prompt) ──
  // If we just submitted but the render hasn't appeared in the list yet, keep the spinner
  if (submitting && !activeRender) {
    return (
      <div className="space-y-3 text-center py-6">
        <Loader2 className="h-6 w-6 animate-spin mx-auto text-fuchsia-500" />
        <div className="text-xs text-slate-500">Submitting render...</div>
        <div className="text-[10px] text-slate-400">The Kontext frame will appear here once processing begins.</div>
      </div>
    );
  }

  if (!activeRender) {
    return (
      <div className="space-y-3">
        {/* Frame info */}
        <div className="flex items-center gap-2 text-[10px] font-mono bg-slate-100 border border-slate-200 rounded-lg px-2.5 py-1.5">
          <Play className="h-3 w-3 text-slate-500" />
          <span className="text-slate-500">Anchor frame:</span>
          <span className="font-bold text-slate-700">{frameNumber}</span>
          <span className="text-slate-400">— paused on this frame</span>
        </div>

        {/* Asset selector */}
        <div>
          <label className="block text-[10px] uppercase tracking-wider font-bold text-slate-500 mb-1 font-mono">
            Brand Asset
          </label>
          <select
            value={assetId}
            onChange={(e) => setAssetId(e.target.value)}
            className="w-full border border-slate-200 rounded-lg px-2.5 py-1.5 text-xs bg-white"
          >
            <option value="">Select an asset...</option>
            {campaignAssets.map(a => (
              <option key={a.id} value={a.id}>{a.name}</option>
            ))}
          </select>
        </div>

        {/* Prompt */}
        <div>
          <label className="block text-[10px] uppercase tracking-wider font-bold text-slate-500 mb-1 font-mono">
            Placement Prompt
          </label>
          <textarea
            value={promptText}
            onChange={(e) => { setPromptText(e.target.value); setSuggestion(null); }}
            placeholder="e.g. Place the brand logo naturally on the billboard, matching lighting and perspective..."
            rows={3}
            className="w-full border border-slate-200 rounded-lg px-2.5 py-1.5 text-xs resize-none"
          />
        </div>

        {/* Gemini prompt suggestion — looks at the actual frame + asset, not just the text */}
        <button
          type="button"
          disabled={suggesting || !assetId || !promptText.trim()}
          onClick={handleSuggestPrompt}
          className="w-full inline-flex items-center justify-center gap-1.5 px-3 py-1.5 rounded-lg text-[10px] font-mono font-bold tracking-wider uppercase border border-blue-200 text-blue-700 hover:bg-blue-50 disabled:opacity-40 cursor-pointer"
        >
          {suggesting ? (
            <><Loader2 className="h-3.5 w-3.5 animate-spin" /> Asking Gemini...</>
          ) : (
            <><Wand2 className="h-3.5 w-3.5" /> Suggest with Gemini</>
          )}
        </button>

        {suggestion && (
          <div className="space-y-1.5 bg-blue-50/50 border border-blue-100 rounded-lg p-2.5">
            <div className="text-[9px] uppercase tracking-wider font-bold text-blue-700 font-mono">
              Gemini looked at the frame + asset — pick which prompt to use
            </div>
            <button
              type="button"
              onClick={() => setSuggestion(null)}
              className={`w-full text-left p-2 rounded-lg border text-[10px] cursor-pointer transition-colors ${
                promptText === suggestion.original ? 'border-blue-400 bg-white' : 'border-slate-200 bg-white hover:border-blue-300'
              }`}
            >
              <span className="font-bold text-slate-500 font-mono uppercase text-[9px] block mb-0.5">Your original</span>
              {suggestion.original}
            </button>
            <button
              type="button"
              onClick={() => { setPromptText(suggestion.suggested); setSuggestion(null); }}
              className="w-full text-left p-2 rounded-lg border border-blue-300 bg-white hover:border-blue-400 text-[10px] cursor-pointer transition-colors"
            >
              <span className="font-bold text-blue-600 font-mono uppercase text-[9px] block mb-0.5">Gemini's suggestion — click to use</span>
              {suggestion.suggested}
            </button>
          </div>
        )}

        {/* Compositing model — Nano Banana Pro (Gemini 3 Pro Image) tends to integrate lighting/
            shadows/depth more convincingly; FLUX Kontext is comparatively stronger at identity
            preservation. Pick per-generation to A/B compare on the same frame. */}
        <div>
          <label className="block text-[10px] uppercase tracking-wider font-bold text-slate-500 mb-1 font-mono">
            Compositing Model
          </label>
          <div className="grid grid-cols-2 gap-1.5 p-0.5 bg-slate-100 rounded-lg">
            <button
              type="button"
              onClick={() => setProvider('flux-kontext')}
              className={`px-2.5 py-1.5 rounded-md text-[10px] font-mono font-bold uppercase tracking-wider cursor-pointer transition-colors ${
                provider === 'flux-kontext' ? 'bg-white text-fuchsia-700 shadow-sm' : 'text-slate-500 hover:text-slate-700'
              }`}
            >
              FLUX Kontext
            </button>
            <button
              type="button"
              onClick={() => setProvider('nano-banana-pro')}
              className={`px-2.5 py-1.5 rounded-md text-[10px] font-mono font-bold uppercase tracking-wider cursor-pointer transition-colors ${
                provider === 'nano-banana-pro' ? 'bg-white text-amber-700 shadow-sm' : 'text-slate-500 hover:text-slate-700'
              }`}
            >
              Nano Banana Pro
            </button>
          </div>
          <div className="text-[9px] text-slate-400 mt-1">
            {provider === 'nano-banana-pro'
              ? 'Better lighting/shadow/depth integration — costs more per generation.'
              : 'Stronger identity preservation of the brand asset itself.'}
          </div>
        </div>

        {/* Quick mode — replaces the old separate one-shot "Anchor & Generate" flow. When on,
            skips the frame-review pause and sends straight to Kling once the Kontext frame is
            ready, trading the review step for speed. */}
        <label className="flex items-start gap-2 p-2.5 rounded-lg border border-slate-200 bg-slate-50 cursor-pointer">
          <input
            type="checkbox"
            checked={quickMode}
            onChange={(e) => setQuickMode(e.target.checked)}
            className="mt-0.5 cursor-pointer"
          />
          <span className="text-[10px] text-slate-600 leading-relaxed">
            <span className="font-bold text-slate-700">⚡ Quick mode</span> — skip reviewing the composited frame and send straight to Kling. Faster, but you won't get a chance to catch a bad frame before it propagates across the scene.
          </span>
        </label>

        {error && (
          <div className="text-[10px] text-red-600 bg-red-50 p-2 rounded-lg border border-red-200">{error}</div>
        )}

        {/* Generate button */}
        <button
          type="button"
          disabled={submitting || !assetId || !promptText.trim()}
          onClick={handleGenerateKontext}
          className={`w-full inline-flex items-center justify-center gap-2 px-4 py-2 rounded-lg text-xs font-mono font-bold tracking-wider uppercase transition-all cursor-pointer ${
            submitting
              ? 'bg-fuchsia-100 text-fuchsia-500'
              : 'bg-fuchsia-600 hover:bg-fuchsia-500 text-white shadow-sm'
          }`}
        >
          {submitting ? (
            <><Loader2 className="h-4 w-4 animate-spin" /> Generating Frame...</>
          ) : (
            <><Image className="h-4 w-4" /> Generate Kontext Frame</>
          )}
        </button>

        {/* Divider */}
        <div className="flex items-center gap-2 text-[9px] text-slate-300 font-mono uppercase tracking-wider">
          <div className="flex-1 h-px bg-slate-200" /> or <div className="flex-1 h-px bg-slate-200" />
        </div>

        {/* Upload a reference frame you already have — skips FLUX.1 Kontext generation entirely,
            useful for keeping a clip consistent with a placement produced elsewhere. */}
        <input
          ref={fileInputRef}
          type="file"
          accept="image/*"
          className="hidden"
          onChange={handleUploadFrame}
        />
        <button
          type="button"
          disabled={uploadingFrame || !assetId}
          onClick={() => fileInputRef.current?.click()}
          className={`w-full inline-flex items-center justify-center gap-1.5 px-3 py-2 rounded-lg text-[10px] font-mono font-bold tracking-wider uppercase border transition-colors cursor-pointer disabled:opacity-40 ${
            uploadingFrame
              ? 'border-slate-200 text-slate-400'
              : 'border-slate-200 text-slate-600 hover:bg-slate-50'
          }`}
        >
          {uploadingFrame ? (
            <><Loader2 className="h-3.5 w-3.5 animate-spin" /> Uploading...</>
          ) : (
            <><Upload className="h-3.5 w-3.5" /> Upload Reference Frame</>
          )}
        </button>
        <div className="text-[9px] text-slate-400 text-center -mt-1">
          Already have a composited frame? Select an asset above, then upload it directly and skip straight to Kling.
        </div>
      </div>
    );
  }

  // ── Step: Generating Kontext frame ──
  if (isKontextGenerating && activeRender.renderMode === 'KontextStep') {
    return (
      <div className="space-y-3 text-center py-6">
        <Loader2 className="h-6 w-6 animate-spin mx-auto text-fuchsia-500" />
        <div className="text-xs text-slate-500">
          Compositing asset onto frame {frameNumber} with FLUX.1 Kontext...
        </div>
        <div className="w-full bg-slate-200 rounded-full h-2 overflow-hidden">
          <div
            className="bg-fuchsia-500 h-full rounded-full transition-all duration-500 ease-out"
            style={{ width: `${activeRender.progress}%` }}
          />
        </div>
      </div>
    );
  }

  // ── Step: Quick mode auto-propagating — frame is ready but we're skipping the review pause ──
  if (isKontextReady && quickMode) {
    return (
      <div className="space-y-3 text-center py-6">
        <Loader2 className="h-6 w-6 animate-spin mx-auto text-fuchsia-500" />
        <div className="text-xs text-slate-500">Quick mode — frame composited, sending straight to Kling...</div>
      </div>
    );
  }

  // ── Step: Review Kontext frame ──
  if (isKontextReady && activeRender.kontextFrameStorageKey) {
    const frameUrl = activeRender.kontextFrameStorageKey;
    return (
      <div className="space-y-3">
        <div className="text-[10px] font-mono bg-fuchsia-50/50 text-fuchsia-700 border border-fuchsia-100/50 p-2.5 rounded-lg">
          Kontext frame ready for Scene #{currentScene.sceneIndex} at frame {frameNumber}. Review before sending to Kling.
        </div>

        {/* Preview the composited frame — click to view full-screen */}
        <div
          className="relative group bg-slate-100 rounded-lg overflow-hidden border border-slate-200 cursor-zoom-in"
          onClick={() => setFullscreenImage(frameUrl)}
        >
          <img
            src={frameUrl}
            alt="Kontext composited frame"
            className="w-full object-contain max-h-48"
            onError={(e) => { (e.target as HTMLImageElement).style.display = 'none'; }}
          />
          <div className="absolute inset-0 bg-black/0 group-hover:bg-black/20 transition-colors flex items-center justify-center">
            <Maximize2 className="h-5 w-5 text-white opacity-0 group-hover:opacity-100 transition-opacity drop-shadow" />
          </div>
        </div>
        {fullscreenModal}

        {/* Updated Kontext prompt for regeneration */}
        <div>
          <label className="block text-[10px] uppercase tracking-wider font-bold text-slate-500 mb-1 font-mono">
            Brand Asset
          </label>
          <select
            value={assetId}
            onChange={(e) => setAssetId(e.target.value)}
            className="w-full border border-slate-200 rounded-lg px-2.5 py-1.5 text-xs bg-white"
          >
            <option value="">Select an asset...</option>
            {campaignAssets.map(a => (
              <option key={a.id} value={a.id}>{a.name}</option>
            ))}
          </select>
        </div>

        {/* Updated Kontext prompt for regeneration */}
        <div>
          <label className="block text-[10px] uppercase tracking-wider font-bold text-slate-500 mb-1 font-mono">
            Updated Kontext Prompt <span className="text-slate-300">(change and regenerate if not happy with the frame)</span>
          </label>
          <textarea
            value={promptText}
            onChange={(e) => setPromptText(e.target.value)}
            placeholder="Adjust your placement instructions..."
            rows={2}
            className="w-full border border-slate-200 rounded-lg px-2.5 py-1.5 text-xs resize-none"
          />
        </div>

        {/* Compositing model — switch and regenerate to A/B compare against the frame above. */}
        <div>
          <label className="block text-[10px] uppercase tracking-wider font-bold text-slate-500 mb-1 font-mono">
            Compositing Model
          </label>
          <div className="grid grid-cols-2 gap-1.5 p-0.5 bg-slate-100 rounded-lg">
            <button
              type="button"
              onClick={() => setProvider('flux-kontext')}
              className={`px-2.5 py-1.5 rounded-md text-[10px] font-mono font-bold uppercase tracking-wider cursor-pointer transition-colors ${
                provider === 'flux-kontext' ? 'bg-white text-fuchsia-700 shadow-sm' : 'text-slate-500 hover:text-slate-700'
              }`}
            >
              FLUX Kontext
            </button>
            <button
              type="button"
              onClick={() => setProvider('nano-banana-pro')}
              className={`px-2.5 py-1.5 rounded-md text-[10px] font-mono font-bold uppercase tracking-wider cursor-pointer transition-colors ${
                provider === 'nano-banana-pro' ? 'bg-white text-amber-700 shadow-sm' : 'text-slate-500 hover:text-slate-700'
              }`}
            >
              Nano Banana Pro
            </button>
          </div>
        </div>

        {/* Kling prompt */}
        <div>
          <label className="block text-[10px] uppercase tracking-wider font-bold text-slate-500 mb-1 font-mono">
            Kling Propagation Prompt <span className="text-slate-300">(optional — leave blank to keep using the placement prompt above)</span>
          </label>
          <textarea
            value={klingPrompt}
            onChange={(e) => setKlingPrompt(e.target.value)}
            placeholder={DEFAULT_KLING_PROPAGATION_PROMPT}
            rows={2}
            className="w-full border border-slate-200 rounded-lg px-2.5 py-1.5 text-xs resize-none"
          />
        </div>

        {error && (
          <div className="text-[10px] text-red-600 bg-red-50 p-2 rounded-lg border border-red-200">{error}</div>
        )}

        <div className="flex gap-2">
          <button
            type="button"
            disabled={submitting}
            onClick={handleGenerateKontext}
            className="flex-1 inline-flex items-center justify-center gap-1.5 px-3 py-2 rounded-lg text-[10px] font-mono font-bold tracking-wider uppercase border border-fuchsia-200 text-fuchsia-700 hover:bg-fuchsia-50 cursor-pointer"
          >
            {submitting ? (
              <><Loader2 className="h-3.5 w-3.5 animate-spin" /> Regenerating...</>
            ) : (
              <><RotateCcw className="h-3.5 w-3.5" /> Regenerate Frame</>
            )}
          </button>
          <button
            type="button"
            disabled={submitting}
            onClick={handlePropagateKling}
            className={`flex-1 inline-flex items-center justify-center gap-1.5 px-3 py-2 rounded-lg text-[10px] font-mono font-bold tracking-wider uppercase transition-all cursor-pointer ${
              submitting
                ? 'bg-amber-100 text-amber-500'
                : 'bg-amber-600 hover:bg-amber-500 text-white shadow-sm'
            }`}
          >
            {submitting ? (
              <><Loader2 className="h-3.5 w-3.5 animate-spin" /> Sending...</>
            ) : (
              <><Video className="h-3.5 w-3.5" /> Send to Kling</>
            )}
          </button>
        </div>

        {/* "Regenerate Frame" reuses this render's asset/prompt to redo just the Kontext step.
            This discards it entirely and goes back to a blank setup — a different frame, asset,
            or prompt, not a continuation of the current attempt. */}
        <button
          type="button"
          disabled={submitting}
          onClick={() => { onRenderCreated(null as any); setError(''); }}
          className="w-full inline-flex items-center justify-center gap-1.5 px-3 py-1.5 rounded-lg text-[10px] font-mono font-bold tracking-wider uppercase text-slate-500 hover:bg-slate-50 cursor-pointer"
        >
          <RotateCcw className="h-3 w-3" /> Start Over (discard this attempt)
        </button>
      </div>
    );
  }

  // ── Step: Generating Kling video ──
  if (isKlingGenerating && activeRender.renderMode === 'KontextStep' && activeRender.progress < 85) {
    return (
      <div className="space-y-3 text-center py-6">
        <Loader2 className="h-6 w-6 animate-spin mx-auto text-amber-500" />
        <div className="text-xs text-slate-500">
          Kling O1 is propagating the edit across the scene...
        </div>
        <div className="w-full bg-slate-200 rounded-full h-2 overflow-hidden">
          <div
            className="bg-amber-500 h-full rounded-full transition-all duration-500 ease-out"
            style={{ width: `${activeRender.progress}%` }}
          />
        </div>
      </div>
    );
  }

  // ── Step: Review Kling video preview ──
  if (isPreviewReady && activeRender.previewStorageKey) {
    const previewUrl = activeRender.previewStorageKey;
    return (
      <div className="space-y-3">
        <div className="text-[10px] font-mono bg-amber-50/50 text-amber-700 border border-amber-100/50 p-2.5 rounded-lg">
          Preview ready for Scene #{currentScene.sceneIndex} — review the Kling output before merging.
        </div>

        {/* Preview video player */}
        <div className="bg-black rounded-lg overflow-hidden">
          <video
            key={activeRender.id}
            src={previewUrl}
            controls
            className="w-full max-h-48"
            preload="metadata"
          />
        </div>

        {/* Updated prompt for redo */}
        <div>
          <label className="block text-[10px] uppercase tracking-wider font-bold text-slate-500 mb-1 font-mono">
            Updated Prompt <span className="text-slate-300">(for redo — leave blank to keep using the same placement prompt)</span>
          </label>
          <textarea
            value={klingPrompt}
            onChange={(e) => setKlingPrompt(e.target.value)}
            placeholder={DEFAULT_KLING_PROPAGATION_PROMPT}
            rows={2}
            className="w-full border border-slate-200 rounded-lg px-2.5 py-1.5 text-xs resize-none"
          />
        </div>

        {error && (
          <div className="text-[10px] text-red-600 bg-red-50 p-2 rounded-lg border border-red-200">{error}</div>
        )}

        <div className="flex gap-2">
          <button
            type="button"
            disabled={submitting}
            onClick={handlePropagateKling}
            className="flex-1 inline-flex items-center justify-center gap-1.5 px-3 py-2 rounded-lg text-[10px] font-mono font-bold tracking-wider uppercase border border-amber-200 text-amber-700 hover:bg-amber-50 cursor-pointer"
          >
            {submitting ? (
              <><Loader2 className="h-3.5 w-3.5 animate-spin" /> Redoing...</>
            ) : (
              <><RotateCcw className="h-3.5 w-3.5" /> Redo Kling</>
            )}
          </button>
          <button
            type="button"
            disabled={submitting}
            onClick={handleApprove}
            className="flex-1 inline-flex items-center justify-center gap-1.5 px-3 py-2 rounded-lg text-[10px] font-mono font-bold tracking-wider uppercase bg-emerald-600 hover:bg-emerald-500 text-white shadow-sm cursor-pointer"
          >
            {submitting ? (
              <><Loader2 className="h-3.5 w-3.5 animate-spin" /> Merging...</>
            ) : (
              <><CheckCircle className="h-3.5 w-3.5" /> Approve & Merge</>
            )}
          </button>
        </div>

        {/* "Redo Kling" reuses this render's Kontext frame and re-propagates. This discards the
            whole attempt (Kontext frame included) and goes back to a blank setup. */}
        <button
          type="button"
          disabled={submitting}
          onClick={() => { onRenderCreated(null as any); setError(''); }}
          className="w-full inline-flex items-center justify-center gap-1.5 px-3 py-1.5 rounded-lg text-[10px] font-mono font-bold tracking-wider uppercase text-slate-500 hover:bg-slate-50 cursor-pointer"
        >
          <RotateCcw className="h-3 w-3" /> Start Over (discard this attempt)
        </button>
      </div>
    );
  }

  // ── Step: Finished — spliced into the source video, ready to be queued for the final render ──
  if (isFinished) {
    const playUrl = activeRender!.storageKey;
    return (
      <div className="space-y-3">
        <div className="text-[10px] font-mono bg-emerald-50/50 text-emerald-700 border border-emerald-100/50 p-2.5 rounded-lg flex items-center gap-1.5">
          <CheckCircle className="h-3.5 w-3.5 shrink-0" />
          Scene #{currentScene.sceneIndex} finished — spliced into the source video.
        </div>

        {playUrl && (
          <video
            key={activeRender!.id}
            src={playUrl}
            controls
            className="w-full max-h-48 rounded-lg bg-black"
            preload="metadata"
          />
        )}

        {error && (
          <div className="text-[10px] text-red-600 bg-red-50 p-2 rounded-lg border border-red-200">{error}</div>
        )}

        <div className="flex gap-2">
          {onSetRenderQueuedForFinal && (
            <button
              type="button"
              disabled={queuing}
              onClick={handleQueue}
              className={`flex-1 inline-flex items-center justify-center gap-1.5 px-3 py-2 rounded-lg text-[10px] font-mono font-bold tracking-wider uppercase transition-all cursor-pointer disabled:opacity-50 ${
                activeRender!.isQueuedForFinal
                  ? 'bg-blue-600 hover:bg-blue-500 text-white shadow-sm'
                  : 'bg-white border border-blue-300 text-blue-600 hover:bg-blue-50'
              }`}
              title="Use this render for this scene in the final combined video"
            >
              {queuing ? (
                <><Loader2 className="h-3.5 w-3.5 animate-spin" /> Updating...</>
              ) : (
                <><CheckCircle className="h-3.5 w-3.5" /> {activeRender!.isQueuedForFinal ? 'Queued for Final Video' : 'Queue for Final Video'}</>
              )}
            </button>
          )}
          {playUrl && (
            <a
              href={playUrl}
              download
              className="inline-flex items-center justify-center gap-1.5 px-3 py-2 rounded-lg text-[10px] font-mono font-bold tracking-wider uppercase border border-slate-200 text-slate-600 hover:bg-slate-50"
            >
              Download
            </a>
          )}
        </div>

        <button
          type="button"
          disabled={submitting}
          onClick={() => { onRenderCreated(null as any); setError(''); }}
          className="w-full inline-flex items-center justify-center gap-1.5 px-3 py-1.5 rounded-lg text-[10px] font-mono font-bold tracking-wider uppercase text-slate-500 hover:bg-slate-50 cursor-pointer"
        >
          <RotateCcw className="h-3 w-3" /> Start Over (new attempt)
        </button>
      </div>
    );
  }

  // ── Step: Failed ──
  // A failure can happen after an earlier stage already succeeded — e.g. Kling generated a
  // usable preview video but the later splice/merge step failed. That artifact (previewStorageKey
  // or kontextFrameStorageKey) is preserved on the render row, so recover it here instead of
  // forcing a full restart that throws away an already-paid-for Kling generation.
  if (isFailed) {
    const recoverablePreview = activeRender.previewStorageKey;
    const recoverableFrame = !recoverablePreview ? activeRender.kontextFrameStorageKey : null;

    return (
      <div className="space-y-3">
        <div className="flex items-start gap-1.5 text-xs text-red-600 bg-red-50 p-2.5 rounded-lg border border-red-200">
          <AlertTriangle className="h-4 w-4 shrink-0 mt-0.5" />
          <span>{activeRender.lastErrorMessage || 'Render failed.'}</span>
        </div>

        {recoverablePreview && (
          <>
            <div className="text-[10px] font-mono bg-amber-50/50 text-amber-700 border border-amber-100/50 p-2.5 rounded-lg">
              Kling already generated a preview before this failed — no need to redo that step.
            </div>
            <div className="bg-black rounded-lg overflow-hidden">
              <video key={activeRender.id} src={recoverablePreview} controls className="w-full max-h-48" preload="metadata" />
            </div>
            {error && <div className="text-[10px] text-red-600 bg-red-50 p-2 rounded-lg border border-red-200">{error}</div>}
            <button
              type="button"
              disabled={submitting}
              onClick={handleApprove}
              className={`w-full inline-flex items-center justify-center gap-1.5 px-3 py-2 rounded-lg text-[10px] font-mono font-bold tracking-wider uppercase transition-all cursor-pointer ${
                submitting ? 'bg-emerald-100 text-emerald-500' : 'bg-emerald-600 hover:bg-emerald-500 text-white shadow-sm'
              }`}
            >
              {submitting ? (<><Loader2 className="h-3.5 w-3.5 animate-spin" /> Retrying...</>) : (<><CheckCircle className="h-3.5 w-3.5" /> Retry Approve & Merge</>)}
            </button>
          </>
        )}

        {recoverableFrame && (
          <>
            <div className="text-[10px] font-mono bg-amber-50/50 text-amber-700 border border-amber-100/50 p-2.5 rounded-lg">
              The Kontext frame was composited before this failed — no need to regenerate it.
            </div>
            <div
              className="relative group bg-slate-100 rounded-lg overflow-hidden border border-slate-200 cursor-zoom-in"
              onClick={() => setFullscreenImage(recoverableFrame)}
            >
              <img src={recoverableFrame} alt="Kontext composited frame" className="w-full object-contain max-h-48" />
              <div className="absolute inset-0 bg-black/0 group-hover:bg-black/20 transition-colors flex items-center justify-center">
                <Maximize2 className="h-5 w-5 text-white opacity-0 group-hover:opacity-100 transition-opacity drop-shadow" />
              </div>
            </div>
            {fullscreenModal}
            {error && <div className="text-[10px] text-red-600 bg-red-50 p-2 rounded-lg border border-red-200">{error}</div>}
            <button
              type="button"
              disabled={submitting}
              onClick={handlePropagateKling}
              className={`w-full inline-flex items-center justify-center gap-1.5 px-3 py-2 rounded-lg text-[10px] font-mono font-bold tracking-wider uppercase transition-all cursor-pointer ${
                submitting ? 'bg-amber-100 text-amber-500' : 'bg-amber-600 hover:bg-amber-500 text-white shadow-sm'
              }`}
            >
              {submitting ? (<><Loader2 className="h-3.5 w-3.5 animate-spin" /> Sending...</>) : (<><Video className="h-3.5 w-3.5" /> Retry Send to Kling</>)}
            </button>
          </>
        )}

        <button
          type="button"
          disabled={submitting}
          onClick={() => { onRenderCreated(null as any); setError(''); }}
          className="w-full inline-flex items-center justify-center gap-1 px-3 py-1.5 rounded-lg text-[10px] font-mono font-bold uppercase border border-slate-200 text-slate-600 hover:bg-slate-50 cursor-pointer"
        >
          <RotateCcw className="h-3 w-3" /> Start Over (discard this attempt)
        </button>
      </div>
    );
  }

  return (
    <div className="space-y-3 text-center py-6">
      <Loader2 className="h-6 w-6 animate-spin mx-auto text-slate-400" />
      <div className="text-xs text-slate-400">Processing...</div>
    </div>
  );
};
