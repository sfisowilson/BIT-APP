import React from 'react';
import { CheckCircle, Circle, Loader2 } from 'lucide-react';

interface PipelineStep {
  id: string;
  label: string;
  complete: boolean;
}

interface PipelineProgressProps {
  steps: PipelineStep[];
}

export const PipelineProgress: React.FC<PipelineProgressProps> = ({ steps }) => {
  return (
    <div className="flex items-center gap-0.5">
      {steps.map((step, idx) => (
        <React.Fragment key={step.id}>
          {idx > 0 && (
            <div className={`h-0.5 w-6 rounded-full ${step.complete ? 'bg-emerald-400' : 'bg-slate-200'}`} />
          )}
          <div className="flex items-center gap-1">
            {step.complete ? (
              <CheckCircle className="h-3.5 w-3.5 text-emerald-500" />
            ) : (
              <Circle className="h-3.5 w-3.5 text-slate-300" />
            )}
            <span className={`text-[10px] font-mono font-bold ${
              step.complete ? 'text-emerald-600' : 'text-slate-400'
            }`}>
              {step.label}
            </span>
          </div>
        </React.Fragment>
      ))}
    </div>
  );
};

/** Compute pipeline step completion state from campaign data */
export function computePipelineSteps(
  hasAssets: boolean,
  hasContent: boolean,
  hasApprovedPlacements: boolean,
  hasRenders: boolean,
): PipelineStep[] {
  return [
    { id: 'assets', label: 'Assets', complete: hasAssets },
    { id: 'content', label: 'Content', complete: hasContent },
    { id: 'placements', label: 'Placements', complete: hasApprovedPlacements },
    { id: 'renders', label: 'Renders', complete: hasRenders },
    { id: 'reports', label: 'Reports', complete: hasRenders },
  ];
}
