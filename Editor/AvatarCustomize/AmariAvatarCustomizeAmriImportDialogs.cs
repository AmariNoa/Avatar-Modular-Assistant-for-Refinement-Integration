using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;

// ReSharper disable once CheckNamespace
namespace com.amari_noa.avatar_modular_assistant.editor
{
    public partial class AmariAvatarCustomizeWindow
    {
        internal enum AmriDuplicateBatchChoice
        {
            OverwriteAll,
            SkipAll,
            Individual,
            CancelAll
        }

        internal enum AmriDuplicateItemChoice
        {
            Overwrite,
            Skip,
            CancelAll
        }

        internal enum AmriUnmanagedMoveBatchChoice
        {
            MoveAll,
            KeepAll,
            Individual,
            CancelAll
        }

        internal enum AmriUnmanagedMoveItemChoice
        {
            Move,
            Keep,
            CancelAll
        }

        private static AmriDuplicateBatchChoice ShowAmriDuplicateBatchDialog(int duplicateCount)
        {
            var title = Localize(
                "amari.window.avatarCustomize.amriImport.duplicateBatch.title",
                "AMRI Import: Duplicate Files");
            var message = string.Format(
                Localize(
                    "amari.window.avatarCustomize.amriImport.duplicateBatch.message",
                    "{0} file(s) already exist at the destination path. How would you like to proceed?"),
                duplicateCount);
            var overwriteAll = Localize(
                "amari.window.avatarCustomize.amriImport.duplicateBatch.overwriteAll",
                "Overwrite All");
            var skipAll = Localize(
                "amari.window.avatarCustomize.amriImport.duplicateBatch.skipAll",
                "Skip All");
            var individual = Localize(
                "amari.window.avatarCustomize.amriImport.duplicateBatch.individual",
                "Choose Individually");

            var result = EditorUtility.DisplayDialogComplex(title, message, overwriteAll, skipAll, individual);
            return result switch
            {
                0 => AmriDuplicateBatchChoice.OverwriteAll,
                1 => AmriDuplicateBatchChoice.SkipAll,
                2 => AmriDuplicateBatchChoice.Individual,
                _ => AmriDuplicateBatchChoice.CancelAll
            };
        }

        private static AmriDuplicateItemChoice ShowAmriDuplicateItemDialog(string targetAssetPath)
        {
            var title = Localize(
                "amari.window.avatarCustomize.amriImport.duplicateItem.title",
                "AMRI Import: Duplicate File");
            var message = string.Format(
                Localize(
                    "amari.window.avatarCustomize.amriImport.duplicateItem.message",
                    "A file already exists at:\n{0}\n\nWhat would you like to do?"),
                targetAssetPath ?? string.Empty);
            var overwrite = Localize(
                "amari.window.avatarCustomize.amriImport.duplicateItem.overwrite",
                "Overwrite");
            var skip = Localize(
                "amari.window.avatarCustomize.amriImport.duplicateItem.skip",
                "Skip");
            var cancelAll = Localize(
                "amari.window.avatarCustomize.amriImport.duplicateItem.cancelAll",
                "Cancel All");

            var result = EditorUtility.DisplayDialogComplex(title, message, overwrite, skip, cancelAll);
            return result switch
            {
                0 => AmriDuplicateItemChoice.Overwrite,
                1 => AmriDuplicateItemChoice.Skip,
                _ => AmriDuplicateItemChoice.CancelAll
            };
        }

        private static AmriUnmanagedMoveBatchChoice ShowAmriUnmanagedMoveBatchDialog(int unmanagedCount)
        {
            var title = Localize(
                "amari.window.avatarCustomize.amriImport.unmanagedMoveBatch.title",
                "AMRI Import: Move to Managed Folder?");
            var message = string.Format(
                Localize(
                    "amari.window.avatarCustomize.amriImport.unmanagedMoveBatch.message",
                    "{0} amri file(s) were placed outside of Assets/_AMARI_DATA/Items/.\nMove them into the managed folder?"),
                unmanagedCount);
            var moveAll = Localize(
                "amari.window.avatarCustomize.amriImport.unmanagedMoveBatch.moveAll",
                "Move All");
            var keepAll = Localize(
                "amari.window.avatarCustomize.amriImport.unmanagedMoveBatch.keepAll",
                "Keep All");
            var individual = Localize(
                "amari.window.avatarCustomize.amriImport.unmanagedMoveBatch.individual",
                "Choose Individually");

            var result = EditorUtility.DisplayDialogComplex(title, message, moveAll, keepAll, individual);
            return result switch
            {
                0 => AmriUnmanagedMoveBatchChoice.MoveAll,
                1 => AmriUnmanagedMoveBatchChoice.KeepAll,
                2 => AmriUnmanagedMoveBatchChoice.Individual,
                _ => AmriUnmanagedMoveBatchChoice.CancelAll
            };
        }

        private static AmriUnmanagedMoveItemChoice ShowAmriUnmanagedMoveItemDialog(string assetPath)
        {
            var title = Localize(
                "amari.window.avatarCustomize.amriImport.unmanagedMoveItem.title",
                "AMRI Import: Move This File?");
            var message = string.Format(
                Localize(
                    "amari.window.avatarCustomize.amriImport.unmanagedMoveItem.message",
                    "Move the following file into the managed folder?\n{0}"),
                assetPath ?? string.Empty);
            var move = Localize(
                "amari.window.avatarCustomize.amriImport.unmanagedMoveItem.move",
                "Move");
            var keep = Localize(
                "amari.window.avatarCustomize.amriImport.unmanagedMoveItem.keep",
                "Keep");
            var cancelAll = Localize(
                "amari.window.avatarCustomize.amriImport.unmanagedMoveItem.cancelAll",
                "Cancel All");

            var result = EditorUtility.DisplayDialogComplex(title, message, move, keep, cancelAll);
            return result switch
            {
                0 => AmriUnmanagedMoveItemChoice.Move,
                1 => AmriUnmanagedMoveItemChoice.Keep,
                _ => AmriUnmanagedMoveItemChoice.CancelAll
            };
        }

        private static void ShowAmriBrokenFilesDialog(IReadOnlyList<string> brokenPaths)
        {
            if (brokenPaths == null || brokenPaths.Count == 0)
            {
                return;
            }

            var title = Localize(
                "amari.window.avatarCustomize.amriImport.broken.title",
                "AMRI Import: Broken Files");
            var header = Localize(
                "amari.window.avatarCustomize.amriImport.broken.message",
                "The following amri files could not be parsed and were skipped:");
            var builder = new StringBuilder();
            builder.AppendLine(header);
            builder.AppendLine();
            foreach (var path in brokenPaths.Where(path => !string.IsNullOrWhiteSpace(path)))
            {
                builder.AppendLine(path);
            }

            EditorUtility.DisplayDialog(title, builder.ToString().TrimEnd(), "OK");
        }

        private static void RollbackPlacedAmriAssets(IReadOnlyList<string> placedAssetPaths)
        {
            if (placedAssetPaths == null || placedAssetPaths.Count == 0)
            {
                return;
            }

            foreach (var assetPath in placedAssetPaths)
            {
                AmariAmriFileUtility.TryDeleteAsset(assetPath);
            }

            AssetDatabase.Refresh();
        }
    }
}
