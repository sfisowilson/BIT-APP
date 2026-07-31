import React from 'react';
import { Loader2, Wand2 } from 'lucide-react';
import {
  SceneItem,
  CreativeAsset,
  RenderItem,
  CreatePromptRenderRequest,
  MIN_PROMPT_EDIT_DURATION_SECONDS,
  MAX_PROMPT_EDIT_DURATION_SECONDS,
} from '../types';

interface PromptGeneratePanelProps {
  currentScene: SceneItem | undefined;
  campaignAssets: CreativeAsset[];
  contentId: string;
  campaignId?: string;
  /** The in-flight or preview-ready render dispatched from this panel, if any. */
  activePromptRender?: RenderItem | null;
  onSubmit: (dto: CreatePromptRenderRequest) => Promise<void>;
  onApprove: (renderId: string) => Promise<void>;
  onReject: (renderId: string) => Promise<void>;
}

/**
 * "AI Placement Assistant → Generate New" mode body. Unlike the "Match to Surface" mode
 * (Gemini auto-pairing an existing detected surface + asset), this generates real new video
 * content via Kling O1 for placements that were never detected as a surface at all — the model
 * infers location purely from the free-text prompt + the asset reference image.
 *
 * No click/quad geometry, no SAM3 tracking. Gated to scenes whose total duration falls within
 * Kling O1's real hard input constraints (MIN/MAX_PROMPT_EDIT_DURATION_SECONDS).
 */
export const PromptGeneratePanel: React.FC<PromptGeneratePanelProps> = ({
  currentScene,
  campaignAssets,
  contentId,
  campaignId,
  activePromptRender,
  onSubmit,
  onApprove,
  onReject,
}) => {
  const [assetId, setAssetId] = React.useState('');
  const [promptText, setPromptText] = React.useState('');
  const [submitting, setSubmitting] = React.useState(false);
  const [actioning, setActioning] = React.useState(false);
  const [actionError, setActionError] = React.useState('');

  // Clear a stale error banner when the user moves to a different scene or render — otherwise
  // it'd keep showing an error that belongs to whatever was previously selected.
  React.useEffect(() => {
    setActionError('');
  }, [currentScene?.id, activePromptRender?.id]);

  // "Processing" covers two different phases: generating the preview clip (progress < 90) and,
  // after approval, splicing the approved clip into the full video (progress >= 90 — preview
  // generation always caps at exactly 90 before flipping to PreviewReady, so Processing at >=90
  // only ever happens post-approval). Distinguished here purely for an accurate status message.
  const isSplicing = activePromptRender?.renderStatus === 'Processing' && (activePromptRender?.progress ?? 0) >= 90;
  const isGenerating = (activePromptRender?.renderStatus === 'Queued' || activePromptRender?.renderStatus === 'Processing') && !isSplicing;
  const isPreviewReady = activePromptRender?.renderStatus === 'PreviewReady';
  const isFailed = activePromptRender?.renderStatus === 'Failed';

  if (!currentScene) {
    return <div className="text-xs text-slate-400 italic text-center py-6">Select a scene above to generate a new AI placement.</div>;
  }

  const isEligible =
    currentScene.durationSeconds >= MIN_PROMPT_EDIT_DURATION_SECONDS &&
    currentScene.durationSeconds <= MAX_PROMPT_EDIT_DURATION_SECONDS;

  if (!isEligible) {
    return (
      <div className="text-[10px] text-amber-600 bg-amber-50 p-2.5 rounded-lg border border-amber-200">
        ⚠️ Scene #{currentScene.sceneIndex} is {currentScene.durationSeconds.toFixed(1)}s — AI-generated placement
        requires a scene between {MIN_PROMPT_EDIT_DURATION_SECONDS}s and {MAX_PROMPT_EDIT_DURATION_SECONDS}s. Pick a
        different scene, or use "Match to Surface" instead.
      </div>
    );
  }

  if (isSplicing && activePromptRender) {
    return (
      <div className="space-y-3 text-center py-6">
        <Loader2 className="h-6 w-6 animate-spin mx-auto text-fuchsia-500" />
        <div className="text-xs text-slate-500">
          Splicing your approved clip into the full video — this can take a few minutes.
        </div>
      </div>
    );
  }

  if (isPreviewReady && activePromptRender) {
    return (
      <div className="space-y-3">
        <div className="text-2xs font-mono bg-fuchsia-50/50 text-fuchsia-700 border border-fuchsia-100/50 p-2.5 rounded-lg">
          Preview ready for Scene #{currentScene.sceneIndex} — review before splicing it into the full video.
        </div>
        <video
          key={activePromptRender.id}
          src={`/api/renders/${activePromptRender.id}/preview`}
          controls
          className="w-full rounded-lg border border-slate-200 bg-black"
        />
        {actionError && (
          <div className="text-[10px] text-red-600 bg-red-50 p-2.5 rounded-lg border border-red-200">
            ⚠️ {actionError}
          </div>
        )}
        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={async () => {
              setActioning(true);
              setActionError('');
              try {
                await onApprove(activePromptRender.id);
              } catch (err: any) {
                setActionError(err.message || 'Failed to approve placement.');
              } finally {
                setActioning(false);
              }
            }}
            disabled={actioning}
            className="flex-1 inline-flex items-center justify-center gap-2 px-3.5 py-2 bg-emerald-600 hover:bg-emerald-500 disabled:bg-slate-300 text-white font-semibold text-xs rounded-lg transition-all cursor-pointer shadow-sm"
          >
            {actioning ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : '✅'} Approve &amp; Splice
          </button>
          <button
            type="button"
            onClick={async () => {
              setActioning(true);
              setActionError('');
              try {
                await onReject(activePromptRender.id);
                setPromptText('');
                setAssetId('');
              } catch (err: any) {
                setActionError(err.message || 'Failed to reject placement.');
              } finally {
                setActioning(false);
              }
            }}
            disabled={actioning}
            className="flex-1 inline-flex items-center justify-center gap-2 px-3.5 py-2 bg-slate-100 hover:bg-slate-200 disabled:bg-slate-50 text-slate-600 font-semibold text-xs rounded-lg transition-all cursor-pointer"
          >
            ↺ Reject &amp; Retry
          </button>
        </div>
      </div>
    );
  }

  return (
    <div className="space-y-4">
      <div className="text-2xs font-mono bg-fuchsia-50/50 text-fuchsia-700 border border-fuchsia-100/50 p-2.5 rounded-lg">
        Scene #{currentScene.sceneIndex} · {currentScene.durationSeconds.toFixed(1)}s · {campaignAssets.length} campaign assets available
      </div>

      <div>
        <label className="block text-[10px] uppercase tracking-wider font-bold text-slate-500 mb-1.5 font-mono">
          Brand Asset
        </label>
        <select
          value={assetId}
          onChange={(e) => setAssetId(e.target.value)}
          disabled={isGenerating}
          className="w-full bg-slate-50 border border-slate-200 rounded-lg px-2.5 py-2 text-xs text-slate-800 focus:outline-none disabled:opacity-60"
        >
          <option value="">Select asset…</option>
          {campaignAssets.map(a => (
            <option key={a.id} value={a.id}>{a.name}</option>
          ))}
        </select>
      </div>

      <div>
        <label className="block text-[10px] uppercase tracking-wider font-bold text-slate-500 mb-1.5 font-mono">
          Placement Instructions
        </label>
        <textarea
          value={promptText}
          onChange={(e) => setPromptText(e.target.value)}
          placeholder='e.g. "On the white wall next to the counter, add a mounted TV with the provided asset brand"'
          rows={3}
          disabled={isGenerating}
          className="w-full bg-slate-50 border border-slate-200 rounded-lg p-2.5 text-xs text-slate-800 focus:outline-none focus:border-fuchsia-500/50 resize-none font-sans disabled:opacity-60"
        />
      </div>

      {campaignAssets.length === 0 && (
        <div className="text-[10px] text-amber-600 bg-amber-50 p-2 rounded-lg border border-amber-200">
          No campaign assets available. Add assets in the Assets tab first.
        </div>
      )}

      {isFailed && activePromptRender?.lastErrorMessage && (
        <div className="text-[10px] text-red-600 bg-red-50 p-2.5 rounded-lg border border-red-200">
          ⚠️ Last attempt failed: {activePromptRender.lastErrorMessage}
        </div>
      )}

      {actionError && (
        <div className="text-[10px] text-red-600 bg-red-50 p-2.5 rounded-lg border border-red-200">
          ⚠️ {actionError}
        </div>
      )}

      <button
        type="button"
        onClick={async () => {
          if (!promptText.trim() || !assetId || !campaignId) return;
          setSubmitting(true);
          setActionError('');
          try {
            await onSubmit({
              contentId,
              sceneId: currentScene.id,
              campaignId,
              assetId,
              promptText: promptText.trim(),
            });
          } catch (err: any) {
            setActionError(err.message || 'Failed to dispatch prompt placement.');
          } finally {
            setSubmitting(false);
          }
        }}
        disabled={!promptText.trim() || !assetId || !campaignId || submitting || isGenerating || campaignAssets.length === 0}
        className="w-full inline-flex items-center justify-center gap-2 px-3.5 py-2 bg-fuchsia-600 hover:bg-fuchsia-500 disabled:bg-slate-300 text-white font-semibold text-xs rounded-lg transition-all cursor-pointer shadow-sm"
      >
        {isGenerating
          ? <><Loader2 className="h-3.5 w-3.5 animate-spin" /> Generating with Kling O1…</>
          : submitting
          ? <><Loader2 className="h-3.5 w-3.5 animate-spin" /> Submitting…</>
          : <><Wand2 className="h-3.5 w-3.5" /> Generate Preview</>}
      </button>
    </div>
  );
};
