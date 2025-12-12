using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Frends.Smb.MoveFiles.Definitions
{
    /// <summary>
    /// Contains information about a backed-up file used to restore state during a rollback operation.
    /// </summary>
    public class FileBackup
    {
        /// <summary>
        /// The original location of the file before it was moved.
        /// </summary>
        /// <example>reports\report.txt</example>
        public string OriginalPath { get; set; }

        /// <summary>
        /// The location where the file was stored as a backup for rollback purposes.
        /// </summary>
        /// <example>backup\report.txt.bak</example>
        public string BackupPath { get; set; }
    }
}
