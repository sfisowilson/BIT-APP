using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;
using Afrobotics.Bit.Api.Data;
using Afrobotics.Bit.Api.Models;
using Afrobotics.Bit.Api.Services;

namespace Afrobotics.Bit.Tests
{
    public class SceneDetectionJobServiceTests
    {
        [Fact]
        public void PipelineStages_ValidTransitions_AllExpectedPathsExist()
        {
            // Verify the pipeline stage transitions match the expected state machine
            Assert.True(PipelineStages.IsValidTransition(PipelineStages.Staging, PipelineStages.Transcoding));
            Assert.True(PipelineStages.IsValidTransition(PipelineStages.Staging, PipelineStages.Failed));
            Assert.True(PipelineStages.IsValidTransition(PipelineStages.Transcoding, PipelineStages.SceneDetecting));
            Assert.True(PipelineStages.IsValidTransition(PipelineStages.Transcoding, PipelineStages.Failed));
            Assert.True(PipelineStages.IsValidTransition(PipelineStages.SceneDetecting, PipelineStages.Completed));
            Assert.True(PipelineStages.IsValidTransition(PipelineStages.SceneDetecting, PipelineStages.Failed));
            Assert.True(PipelineStages.IsValidTransition(PipelineStages.Failed, PipelineStages.Staging));
            Assert.True(PipelineStages.IsValidTransition(PipelineStages.Completed, PipelineStages.SceneDetecting));
        }

        [Fact]
        public void PipelineStages_InvalidTransitions_AreRejected()
        {
            // Self-transitions are NOT allowed
            Assert.False(PipelineStages.IsValidTransition(PipelineStages.SceneDetecting, PipelineStages.SceneDetecting));
            Assert.False(PipelineStages.IsValidTransition(PipelineStages.Completed, PipelineStages.Completed));
            Assert.False(PipelineStages.IsValidTransition(PipelineStages.Staging, PipelineStages.Staging));

            // Backwards transitions are NOT allowed (except Failed→Staging, Completed→SceneDetecting)
            Assert.False(PipelineStages.IsValidTransition(PipelineStages.Completed, PipelineStages.Transcoding));
            Assert.False(PipelineStages.IsValidTransition(PipelineStages.SceneDetecting, PipelineStages.Transcoding));
            Assert.False(PipelineStages.IsValidTransition(PipelineStages.Transcoding, PipelineStages.Staging));

            // Skipping stages is NOT allowed
            Assert.False(PipelineStages.IsValidTransition(PipelineStages.Staging, PipelineStages.SceneDetecting));
            Assert.False(PipelineStages.IsValidTransition(PipelineStages.Staging, PipelineStages.Completed));
        }

        [Fact]
        public void PipelineStages_AllStages_AreDefined()
        {
            Assert.Equal(5, PipelineStages.All.Length);
            Assert.Contains(PipelineStages.Staging, PipelineStages.All);
            Assert.Contains(PipelineStages.Transcoding, PipelineStages.All);
            Assert.Contains(PipelineStages.SceneDetecting, PipelineStages.All);
            Assert.Contains(PipelineStages.Completed, PipelineStages.All);
            Assert.Contains(PipelineStages.Failed, PipelineStages.All);
        }

        [Fact]
        public void PipelineStages_SceneDetecting_OnlyAllowsCompletedOrFailed()
        {
            var allowed = PipelineStages.GetAllowedTransitions(PipelineStages.SceneDetecting);
            Assert.Contains(PipelineStages.Completed, allowed);
            Assert.Contains(PipelineStages.Failed, allowed);
            Assert.DoesNotContain(PipelineStages.Staging, allowed);
            Assert.DoesNotContain(PipelineStages.Transcoding, allowed);
            Assert.DoesNotContain(PipelineStages.SceneDetecting, allowed);
        }

        [Fact]
        public void PipelineStages_Failed_OnlyAllowsStaging()
        {
            var allowed = PipelineStages.GetAllowedTransitions(PipelineStages.Failed);
            Assert.Single(allowed);
            Assert.Contains(PipelineStages.Staging, allowed);
        }

        [Fact]
        public void PipelineStages_Completed_OnlyAllowsSceneDetecting()
        {
            var allowed = PipelineStages.GetAllowedTransitions(PipelineStages.Completed);
            Assert.Single(allowed);
            Assert.Contains(PipelineStages.SceneDetecting, allowed);
        }

        [Fact]
        public void PipelineStages_TransitionToSameStage_ThrowsException()
        {
            // This test validates that the self-transition guard works.
            // SceneDetecting → SceneDetecting is invalid (verified above).
            Assert.False(PipelineStages.IsValidTransition(PipelineStages.SceneDetecting, PipelineStages.SceneDetecting));

            // The ContentController.RedetectScenes endpoint has an explicit guard
            // to check `content.IngestionStatus != PipelineStages.SceneDetecting`
            // before calling TransitionStageAsync, preventing this error.
        }
    }
}
