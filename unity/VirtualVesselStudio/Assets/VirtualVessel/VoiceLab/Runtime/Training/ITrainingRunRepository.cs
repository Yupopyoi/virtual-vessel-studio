using System.Collections.Generic;
using VirtualVessel.ProjectData.Records;

namespace VirtualVessel.VoiceLab.Training
{
    /// <summary>
    /// Where Training Runs are kept (Voice Lab detailed design 6.3, 8.6). The first one keeps run.json files;
    /// SQLite (system design 13.12) will be another, so nothing else reads the records. A run's large files stay
    /// in its folder either way (system design 13.13). Every operation reports its failure as a result and leaves
    /// handling it to the caller (system design 6.31).
    /// </summary>
    public interface ITrainingRunRepository
    {
        /// <summary>The folder of a run's files: its speech, weights, and output.</summary>
        /// <exception cref="System.ArgumentException">Not a run ID: a bug in the caller.</exception>
        string FolderOf(string runId);

        CrudResult<TrainingRun> Create(RunDesign design);

        CrudResult<TrainingRun> Save(TrainingRun run);

        /// <summary>NotFound when there is no such run.</summary>
        CrudResult<TrainingRun> Get(string runId);

        /// <summary>Every run that can be read, newest first.</summary>
        CrudResult<IReadOnlyList<TrainingRun>> List();

        /// <summary>Deletes the run and its files; a run that is not there succeeds.</summary>
        CrudResult Delete(string runId);
    }
}
