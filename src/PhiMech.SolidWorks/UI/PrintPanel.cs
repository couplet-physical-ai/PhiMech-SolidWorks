// Copyright 2026 PhiMech-SolidWorks contributors. Licensed under the Apache License, Version 2.0.
//
// The Print tab, in the shared design system (UI/Controls). Four cards, top to bottom, each acting
// only when its button is clicked:
//
//   Printer          the Moonraker address (lab configured printer by default) and "Check printer": read-only
//                    GETs that show ready / printing (file, progress) / paused / error, temperatures
//                    and the build volume. Failures say to use your configured local network.
//   Fit              the part's bounding box against the build volume in all six orientations: the
//                    best fit and the limiting axis, or a refusal; plus PhiMech's own reading of the
//                    printer through its CLI when it is installed.
//   Slice            PETG or PLA, then "Slice": SolidWorks exports an STL (mm), the PhiMech CLI slices
//                    it with OrcaSlicer's configured printer profile; time, filament and layers come back and
//                    the .gcode.3mf is kept beside the part in print\.
//   Send to printer  "Upload to printer…", a confirmation naming the printer, file and size, then the
//                    one POST. Never a print: the card ends by saying how a person starts it.
//
// Hosted directly as the optional printer task pane.
// Every SolidWorks call goes through the two callbacks; everything else is Print/PrintFlow.

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using PhiMech.SolidWorks.Commands;
using PhiMech.SolidWorks.Print;
using PhiMech.SolidWorks.UI.Controls;

namespace PhiMech.SolidWorks.UI
{
    public sealed class PrintPanel : UserControl, IAutoHeight
    {
        private readonly PrintFlow _flow;
        private readonly Func<bool, PrintPart> _part;
        private readonly Func<string, string> _exportStl;
        private readonly Func<string, bool> _confirm;

        private readonly Stack _stack;
        // ---- printer -----------------------------------------------------------------------------
        private readonly Card _printerCard;
        private readonly InputBox _url;
        private readonly TextBlock _urlNote;
        private readonly BrandButton _check;
        private readonly BrandButton _useDefault;
        private readonly TextBlock _printerState;
        private readonly Meter _progress;
        private readonly TextBlock _printerFacts;
        // ---- fit ---------------------------------------------------------------------------------
        private readonly Card _fitCard;
        private readonly BrandButton _fit;
        private readonly TextBlock _fitText;
        private readonly TextBlock _phimechLine;
        // ---- slice -------------------------------------------------------------------------------
        private readonly Card _sliceCard;
        private readonly Segmented _material;
        private readonly BrandButton _slice;
        private readonly TextBlock _sliceReason;
        private readonly FlowRow _stats;
        private readonly Tile _timeTile;
        private readonly Tile _filamentTile;
        private readonly Tile _layersTile;
        private readonly TextBlock _sliceText;
        // ---- upload ------------------------------------------------------------------------------
        private readonly Card _uploadCard;
        private readonly BrandButton _upload;
        private readonly TextBlock _uploadReason;
        private readonly TextBlock _uploadText;

        private bool _busy;

        /// <param name="flow">the steps (printer URL settings, HTTP, the PhiMech CLI)</param>
        /// <param name="activePart">the open part as the tab needs it (PrintExport.ActivePart); the flag asks
        /// for the full read (bodies, part box) rather than the cheap one used to enable buttons</param>
        /// <param name="exportStl">writes the open part as a binary mm STL to the path; null or why not</param>
        /// <param name="confirmUpload">asks the person (null: the default confirmation dialog)</param>
        public PrintPanel(PrintFlow flow, Func<bool, PrintPart> activePart, Func<string, string> exportStl, Func<string, bool> confirmUpload = null)
        {
            _flow = flow ?? new PrintFlow(PrintSettings.Load());
            _part = activePart;
            _exportStl = exportStl;
            _confirm = confirmUpload ?? AskUpload;

            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Ui.Page;
            Font = Ui.Body;
            _stack = new Stack(Ui.Space3) { Dock = DockStyle.Top, BackColor = Ui.Page };
            Controls.Add(_stack);
            _stack.SizeChanged += (s, e) => Parent?.PerformLayout();

            // ---- printer ---------------------------------------------------------------------------
            _printerCard = _stack.Add(new Card("Printer", "printer on your configured local network. Checking only reads: it never moves, heats or starts anything."));
            _url = _printerCard.Add(new InputBox(PrinterUrl.Default, accessibleName: "Printer address (Moonraker URL)"), Ui.Space3);
            _urlNote = _printerCard.Add(new TextBlock("", Ui.Caption, Ui.Muted), Ui.Space1);
            var checkRow = _printerCard.Add(new FlowRow(), Ui.Space2);
            _check = checkRow.Add(new BrandButton("Check printer", ButtonKind.Secondary) { BusyText = "Checking…" });
            checkRow.Add(new Spacer(), fill: true);
            _useDefault = checkRow.Add(new BrandButton("Use the default", ButtonKind.Link));
            _printerState = _printerCard.Add(new TextBlock("", Ui.BodyStrong, Ui.Ink), Ui.Space3);
            _progress = _printerCard.Add(new Meter(), Ui.Space1);
            _printerFacts = _printerCard.Add(new TextBlock("", Ui.Caption, Ui.Text), Ui.Space1);
            Reveal.Set(_printerState, false);
            Reveal.Set(_progress, false);
            Reveal.Set(_printerFacts, false);

            // ---- fit -------------------------------------------------------------------------------
            _fitCard = _stack.Add(new Card("Fit", "The part's bounding box against the build volume, in all six orientations."));
            _fit = _fitCard.Add(new BrandButton("Check fit", ButtonKind.Secondary) { BusyText = "Checking…" }, Ui.Space3, Align.Left);
            _fitText = _fitCard.Add(new TextBlock("", Ui.Caption, Ui.Text), Ui.Space2);
            _phimechLine = _fitCard.Add(new TextBlock("", Ui.Caption, Ui.Muted), Ui.Space1);
            Reveal.Set(_fitText, false);
            Reveal.Set(_phimechLine, false);

            // ---- slice -----------------------------------------------------------------------------
            _sliceCard = _stack.Add(new Card("Slice", "PhiMech slices with OrcaSlicer's configured printer profile, at the material's stock temperatures."));
            _material = _sliceCard.Add(new Segmented("Material", PrintSlicer.Materials), Ui.Space3);
            _material.Select(0);
            _slice = _sliceCard.Add(new BrandButton("Slice", ButtonKind.Primary) { BusyText = "Slicing…" }, Ui.Space3);
            _sliceReason = _sliceCard.Add(new TextBlock("", Ui.Caption, Ui.Muted), Ui.Space1);
            _stats = _sliceCard.Add(new FlowRow(), Ui.Space3);
            _timeTile = _stats.Add(new Tile("Print time"), fill: true, minWidth: 84);
            _filamentTile = _stats.Add(new Tile("Filament"), fill: true, minWidth: 110);
            _layersTile = _stats.Add(new Tile("Layers"), fill: true, minWidth: 64);
            _sliceText = _sliceCard.Add(new TextBlock("", Ui.Caption, Ui.Text), Ui.Space2);
            Reveal.Set(_sliceReason, false);
            Reveal.Set(_stats, false);
            Reveal.Set(_sliceText, false);

            // ---- upload ----------------------------------------------------------------------------
            _uploadCard = _stack.Add(new Card("Send to printer", "Stores the sliced file on the printer. PhiMech never starts a print."));
            _upload = _uploadCard.Add(new BrandButton("Upload to printer…", ButtonKind.Secondary) { BusyText = "Uploading…" }, Ui.Space3, Align.Left);
            _uploadReason = _uploadCard.Add(new TextBlock("Slice the part first.", Ui.Caption, Ui.Muted), Ui.Space1);
            _uploadText = _uploadCard.Add(new TextBlock("", Ui.Caption, Ui.Text), Ui.Space2);
            Reveal.Set(_uploadText, false);

            // ---- wiring ----------------------------------------------------------------------------
            _check.Click += (s, e) => CheckPrinter();
            _useDefault.Click += (s, e) =>
            {
                _flow.Settings.PrinterUrl = "";
                TrySave();
                ShowUrl();
            };
            _fit.Click += (s, e) => CheckFit();
            _slice.Click += (s, e) => Slice();
            _upload.Click += (s, e) => Upload();
            ShowUrl();
            RefreshContext();
        }

        public int HeightFor(int width) => _stack.HeightFor(width);

        // ---- read-outs for the host, the preview and tests ----------------------------------------

        public string PrinterPill => _printerCard.Pill.Text;
        public string PrinterStateText => _printerState.Text;
        public string PrinterFactsText => _printerFacts.Text;
        public string FitPill => _fitCard.Pill.Text;
        public string FitText => _fitText.Text;
        public string SlicePill => _sliceCard.Pill.Text;
        public string SliceText => _sliceText.Text;
        public string UploadText => _uploadText.Text;
        public bool UploadEnabled => _upload.Enabled;
        public PrintFlow Flow => _flow;

        /// <summary>Enable the buttons for what is open (called by the pane when the document may have changed).</summary>
        public void RefreshContext()
        {
            if (_busy) return;
            var part = SafePart(measure: false);
            var problem = part == null ? "Open a part to print it." : part.Problem;
            _fit.Enabled = part != null && part.IsPart;
            _slice.Enabled = problem == null;
            SetReason(_sliceReason, problem);
            var sliced = _flow.LastSlice != null && _flow.LastSlice.Ok;
            _upload.Enabled = sliced;
            SetReason(_uploadReason, sliced ? null : "Slice the part first.");
        }

        // ---- printer ------------------------------------------------------------------------------

        /// <summary>"Check printer": save an edited address (if valid), then the read-only GETs.</summary>
        public void CheckPrinter() => CheckPrinterThen(null);

        private void CheckPrinterThen(Action<bool> after)
        {
            if (_busy) return;
            if (!ApplyEditedUrl())
            {
                after?.Invoke(false);
                return;
            }
            var choice = _flow.Url;
            if (choice.Url == null)
            {
                ShowPrinterProblem("Not a usable printer address: " + choice.Problem);
                after?.Invoke(false);
                return;
            }
            Busy(_check, true);
            _printerCard.Pill.Set("Checking", Tone.Busy);
            Await(_flow.CheckPrinterAsync(), check =>
            {
                Busy(_check, false);
                ShowCheck(check);
                after?.Invoke(true);
            }, ex =>
            {
                Busy(_check, false);
                ShowPrinterProblem(ex.Message, ex is PrinterException pe && pe.Problem == PrinterProblem.KlippyNotReady ? "Not ready" : "Unreachable");
                after?.Invoke(false);
            });
        }

        private bool ApplyEditedUrl()
        {
            var typed = _url.Text.Trim();
            if (typed.Length == 0) return true;
            if (!PrinterUrl.Normalize(typed, out var normal, out var why))
            {
                ShowPrinterProblem(why, "Bad address");
                return false;
            }
            if (normal == _flow.Url.Url) return true;
            _flow.Settings.PrinterUrl = normal;
            TrySave();
            ShowUrl();
            return true;
        }

        private void TrySave()
        {
            try
            {
                _flow.Settings.Save();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // The address still applies for this session.
            }
        }

        private void ShowUrl()
        {
            var c = _flow.Url;
            _url.Text = c.Url ?? (_flow.Settings.PrinterUrl ?? "");
            _urlNote.Text = c.Url == null
                ? "Refused (" + c.SourceText + "): " + c.Problem
                : c.Source == PrinterUrlSource.Default
                    ? "the configured printer. Type another address to change it, or set " + PrinterUrl.EnvVar + "."
                    : "Address " + c.SourceText + ".";
            _urlNote.ForeColor = c.Url == null ? Ui.AccentText : Ui.Muted;
            Reveal.Set(_useDefault, c.Source == PrinterUrlSource.Settings);
        }

        /// <summary>Show a finished check (also used by the preview with a canned check).</summary>
        public void ShowCheck(PrinterCheck check)
        {
            var info = check.Info;
            var st = check.Status;
            string pill;
            Tone tone;
            string state;
            if (info != null && !info.Ready)
            {
                pill = "Not ready";
                tone = Tone.Danger;
                state = MoonrakerClient.KlippyText(info);
            }
            else if (st == null)
            {
                pill = "Reachable";
                tone = Tone.Warning;
                state = "The printer answered, but its status could not be read" + (check.Warning == null ? "." : ": " + check.Warning);
            }
            else if (st.Printing)
            {
                pill = "Printing " + Percent(st.Progress);
                tone = Tone.Warning;
                state = "Printing " + Named(st.Filename) + Layers(st) + ". You can still upload: it only stores a file.";
            }
            else if (st.Paused)
            {
                pill = "Paused";
                tone = Tone.Warning;
                state = "Paused on " + Named(st.Filename) + " at " + Percent(st.Progress) + ".";
            }
            else if (st.Error)
            {
                pill = "Error";
                tone = Tone.Danger;
                state = "The printer reports an error" + (string.IsNullOrWhiteSpace(st.PrintMessage) ? "." : ": " + st.PrintMessage.Trim());
            }
            else
            {
                pill = "Ready";
                tone = Tone.Success;
                state = "Ready" + (string.IsNullOrEmpty(st.PrintState) || st.PrintState == "standby" ? "" : " (last job " + st.PrintState + ")")
                        + (info?.Hostname != null ? " · " + info.Hostname : "") + ".";
            }
            _printerCard.Pill.Set(pill, tone);
            _printerState.Text = state;
            _printerState.ForeColor = tone == Tone.Danger ? Ui.ToneFore(Tone.Danger) : Ui.Ink;
            Reveal.Set(_printerState, true);
            _progress.Value = st?.Busy == true ? st.Progress ?? 0 : 0;
            Reveal.Set(_progress, st?.Busy == true && st.Progress != null);
            var facts = "";
            if (st != null) facts += "Nozzle " + st.Nozzle.Text + " · Bed " + st.Bed.Text;
            if (check.Envelope != null)
                facts += (facts.Length > 0 ? "\n" : "") + "Build volume " + check.Envelope.Text + " (" + check.Envelope.SourceText + ")";
            if (check.Config?.NozzleDiameterMm != null)
                facts += (facts.Length > 0 ? "\n" : "") + check.Config.NozzleDiameterMm.Value.ToString("0.0#", CultureInfo.InvariantCulture) + " mm nozzle"
                         + (check.Config.ChamberMaxC != null ? " · heated chamber" : "");
            if (check.Warning != null && st != null) facts += (facts.Length > 0 ? "\n" : "") + check.Warning;
            _printerFacts.Text = facts;
            Reveal.Set(_printerFacts, facts.Length > 0);
        }

        private void ShowPrinterProblem(string message, string pill = "Unreachable")
        {
            _printerCard.Pill.Set(pill, Tone.Danger);
            _printerState.Text = message;
            _printerState.ForeColor = Ui.ToneFore(Tone.Danger);
            Reveal.Set(_printerState, true);
            Reveal.Set(_progress, false);
            Reveal.Set(_printerFacts, false);
        }

        private static string Percent(double? p) => p == null ? "?" : Math.Round(p.Value * 100).ToString("0", CultureInfo.InvariantCulture) + "%";

        private static string Named(string file) => string.IsNullOrWhiteSpace(file) ? "a job" : file;

        private static string Layers(PrinterStatus st) =>
            st.CurrentLayer != null && st.TotalLayers != null && st.TotalLayers > 0
                ? " (layer " + st.CurrentLayer.Value.ToString(CultureInfo.InvariantCulture) + " of " + st.TotalLayers.Value.ToString(CultureInfo.InvariantCulture) + ")"
                : "";

        // ---- fit ----------------------------------------------------------------------------------

        /// <summary>"Check fit": SolidWorks' part box now (pure), then PhiMech's own reading in the background.</summary>
        public void CheckFit()
        {
            if (_busy) return;
            var part = SafePart();
            if (part?.ApproxSizeMm == null)
            {
                ShowFitProblem(part?.Problem ?? "Open a part to check its fit.");
                return;
            }
            ShowFit(_flow.Fit(part.ApproxSizeMm), exact: false);
            if (!_flow.CliInstalled)
            {
                SetLine(_phimechLine, "PhiMech's own printer reading needs the PhiMech command-line tools (not installed on this PC).");
                return;
            }
            SetLine(_phimechLine, "Asking PhiMech (phimech print status)…");
            Await(Task.Run(() => _flow.PhiMechReading()), r => ShowPhiMechReading(r, part.ApproxSizeMm),
                ex => SetLine(_phimechLine, "PhiMech's reading failed: " + ex.Message));
        }

        public void ShowFit(FitResult fit, bool exact)
        {
            var tone = !fit.Fits ? Tone.Danger : fit.FitsAsExported ? Tone.Success : Tone.Warning;
            _fitCard.Pill.Set(!fit.Fits ? "Too big" : fit.FitsAsExported ? "Fits" : "On its side", tone);
            _fitText.Text = fit.Detail + (exact ? " Size from the exported STL." : " Size from SolidWorks' part box (approximate); Slice checks the exact STL.");
            _fitText.ForeColor = tone == Tone.Danger ? Ui.ToneFore(Tone.Danger) : Ui.Text;
            Reveal.Set(_fitText, true);
        }

        private void ShowFitProblem(string message)
        {
            _fitCard.Pill.Set("", Tone.Neutral);
            _fitText.Text = message;
            _fitText.ForeColor = Ui.Muted;
            Reveal.Set(_fitText, true);
        }

        public void ShowPhiMechReading(PhiMechPrinterReading r, double[] partMm)
        {
            if (r == null)
            {
                SetLine(_phimechLine, "PhiMech's own printer reading needs the PhiMech command-line tools.");
                return;
            }
            if (r.BuildVolumeMm == null || !r.Reachable || r.BuildVolumeSource != "queried")
            {
                // PhiMech's machine gates fail closed on anything but a queried reading; so does this line.
                SetLine(_phimechLine, "PhiMech could not read the printer" + (string.IsNullOrEmpty(r.Error) ? "" : " (" + r.Error + ")")
                                      + ", so its machine check fails closed: unknown is not a pass.");
                return;
            }
            var verdict = FitCheck.Evaluate(partMm, r.BuildVolumeMm);
            SetLine(_phimechLine, "PhiMech (phimech print status): build volume " + FitCheck.Size(r.BuildVolumeMm) + ", queried from the printer"
                                  + (string.IsNullOrEmpty(r.KlippyState) ? "" : ", Klipper " + r.KlippyState) + ". "
                                  + (verdict.FitsAsExported ? "PASS: fits as modelled." : verdict.Fits ? "Fits only on its side." : "FAIL: too big."));
        }

        // ---- slice --------------------------------------------------------------------------------

        public string Material => _material.SelectedName;

        public void SelectMaterial(string name)
        {
            var i = Array.IndexOf(PrintSlicer.Materials, name);
            if (i >= 0) _material.Select(i);
        }

        /// <summary>"Slice": export the STL here (SolidWorks' thread), slice it in the background.</summary>
        public void Slice()
        {
            if (_busy) return;
            var part = SafePart();
            if (part == null || part.Problem != null)
            {
                ShowSliceProblem(part?.Problem ?? "Open a part to print it.");
                return;
            }
            var material = Material;
            var workDir = PrintFlow.WorkDir(part.Path, part.Title, material, DateTime.UtcNow);
            var stl = Path.Combine(workDir, Contract.Slug.From(part.Title) + ".stl");
            Busy(_slice, true);
            _sliceCard.Pill.Set("Exporting", Tone.Busy);
            Refresh();   // paint the busy state: the export runs on this (SolidWorks') thread
            string problem;
            try
            {
                problem = _exportStl(stl);
            }
            catch (Exception ex)
            {
                problem = "The STL export failed: " + ex.GetType().Name + ": " + ex.Message;
            }
            if (problem != null)
            {
                Busy(_slice, false);
                ShowSliceProblem(problem);
                return;
            }
            _sliceCard.Pill.Set("Slicing", Tone.Busy);
            Await(Task.Run(() => _flow.Slice(stl, part.Path, part.Title, material, workDir)), o =>
            {
                Busy(_slice, false);
                ShowSlice(o);
            }, ex =>
            {
                Busy(_slice, false);
                ShowSliceProblem("Slicing failed: " + ex.Message);
            });
        }

        public void ShowSlice(SliceOutcome o)
        {
            if (o.Fit != null) ShowFit(o.Fit, exact: true);
            if (!o.Ok)
            {
                ShowSliceProblem(o.Message, o.OrcaMissing ? "No slicer" : "Not sliced");
                return;
            }
            _sliceCard.Pill.Set("Sliced", Tone.Success);
            _timeTile.Value = o.Summary.TimeText;
            _filamentTile.Value = o.Summary.FilamentText;
            _layersTile.Value = o.Summary.LayerCount?.ToString(CultureInfo.InvariantCulture) ?? "?";
            Reveal.Set(_stats, true);
            var temps = o.Summary.NozzleTempC != null && o.Summary.BedTempC != null
                ? " Stock " + (o.Summary.FilamentType ?? o.Material) + " temperatures: nozzle " + o.Summary.NozzleTempC + " °C, bed " + o.Summary.BedTempC + " °C."
                : "";
            var warnings = o.Summary.Warnings.Count == 0 ? "" : "\nSlicer notes: " + string.Join(" ", o.Summary.Warnings);
            _sliceText.Text = o.FileName + " (" + PrintFormat.Bytes(o.SizeBytes) + "), kept in " + Path.GetDirectoryName(o.GcodePath) + "." + temps + warnings;
            _sliceText.ForeColor = Ui.Text;
            Reveal.Set(_sliceText, true);
            Reveal.Set(_uploadText, false);
            RefreshContext();
        }

        private void ShowSliceProblem(string message, string pill = "Not sliced")
        {
            _sliceCard.Pill.Set(pill, Tone.Danger);
            _sliceText.Text = message;
            _sliceText.ForeColor = Ui.ToneFore(Tone.Danger);
            Reveal.Set(_sliceText, true);
            Reveal.Set(_stats, false);
            RefreshContext();
        }

        // ---- upload -------------------------------------------------------------------------------

        /// <summary>"Upload to printer…": a fresh read-only check, the confirmation, then the one POST.</summary>
        public void Upload()
        {
            if (_busy) return;
            var slice = _flow.LastSlice;
            if (slice == null || !slice.Ok)
            {
                SetReason(_uploadReason, "Slice the part first.");
                return;
            }
            CheckPrinterThen(reached =>
            {
                if (!reached)
                {
                    ShowUploadProblem("Not uploaded: the printer could not be checked. " + _printerState.Text);
                    return;
                }
                if (!_confirm(_flow.ConfirmationText(slice)))
                {
                    ShowUploadNote("Not uploaded (cancelled).");
                    return;
                }
                Busy(_upload, true);
                _uploadCard.Pill.Set("Uploading", Tone.Busy);
                Await(_flow.UploadAsync(slice), u =>
                {
                    Busy(_upload, false);
                    ShowUploaded(u);
                }, ex =>
                {
                    Busy(_upload, false);
                    ShowUploadProblem(ex.Message);
                });
            });
        }

        public void ShowUploaded(UploadOutcome u)
        {
            _uploadCard.Pill.Set(u.PrintStarted ? "Check printer" : u.AlreadyThere ? "On printer" : "Uploaded", u.PrintStarted ? Tone.Danger : Tone.Success);
            _uploadText.Text = PrintFlow.AfterUploadText(u);
            _uploadText.ForeColor = u.PrintStarted ? Ui.ToneFore(Tone.Danger) : Ui.Text;
            Reveal.Set(_uploadText, true);
            ScrollHelp.IntoView(_uploadCard);
        }

        private void ShowUploadProblem(string message)
        {
            _uploadCard.Pill.Set("Not uploaded", Tone.Danger);
            _uploadText.Text = message;
            _uploadText.ForeColor = Ui.ToneFore(Tone.Danger);
            Reveal.Set(_uploadText, true);
        }

        private void ShowUploadNote(string message)
        {
            _uploadCard.Pill.Set("", Tone.Neutral);
            _uploadText.Text = message;
            _uploadText.ForeColor = Ui.Muted;
            Reveal.Set(_uploadText, true);
        }

        private bool AskUpload(string details)
        {
            using (var d = new UploadDialog(details)) return d.ShowDialog(FindForm()) == DialogResult.OK;
        }

        /// <summary>The confirmation dialog on its own (the UI preview renders it).</summary>
        public static Form CreateUploadDialog(string details) => new UploadDialog(details);

        // ---- plumbing ------------------------------------------------------------------------------

        private PrintPart SafePart(bool measure = true)
        {
            try
            {
                return _part?.Invoke(measure);
            }
            catch (Exception ex)
            {
                return new PrintPart { Problem = "Could not read the open part: " + ex.Message };
            }
        }

        private void Busy(BrandButton button, bool busy)
        {
            _busy = busy;
            button.Busy = busy;
            foreach (var b in new[] { _check, _fit, _slice, _upload })
                if (b != button) b.Enabled = !busy && b.Enabled;
            Cursor = busy ? Cursors.AppStarting : Cursors.Default;
            if (!busy) RefreshContext();
            if (!busy) _check.Enabled = true;
        }

        /// <summary>Continue on the UI thread when the task finishes (SolidWorks stays usable meanwhile).</summary>
        private void Await<T>(Task<T> task, Action<T> done, Action<Exception> failed)
        {
            task.ContinueWith(t =>
            {
                if (IsDisposed) return;
                Action ui = () =>
                {
                    if (t.Status == TaskStatus.RanToCompletion) done(t.Result);
                    else failed(t.Exception?.GetBaseException() ?? new OperationCanceledException("cancelled"));
                };
                try
                {
                    if (IsHandleCreated) BeginInvoke(ui);
                    else ui();
                }
                catch (InvalidOperationException)
                {
                    // SolidWorks is closing
                }
            }, TaskScheduler.Default);
        }

        private static void SetReason(TextBlock line, string text)
        {
            line.Text = text ?? "";
            Reveal.Set(line, !string.IsNullOrEmpty(text));
        }

        private static void SetLine(TextBlock line, string text) => SetReason(line, text);

        /// <summary>The confirmation: printer, file, size, and that nothing is started. Cancel is the default.</summary>
        private sealed class UploadDialog : BrandDialog
        {
            public UploadDialog(string details)
                : base("PhiMech - upload to printer", "Upload to the printer?", "PhiMech stores the file on the printer. It does not start a print.")
            {
                foreach (var line in (details ?? "").Split('\n'))
                    Body.Add(new TextBlock(line, Ui.Body, Ui.Text), Ui.Space1);
                var cancel = AddButton("Cancel", ButtonKind.Secondary);
                var upload = AddButton("Upload", ButtonKind.Primary);
                cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
                upload.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };
                CancelButton = cancel;
                ActiveControl = cancel;
            }
        }

        /// <summary>A number with its label on a subtle tile (a status tile, kept local to this tab).</summary>
        private sealed class Tile : Control, IAutoHeight
        {
            private readonly string _label;
            private string _value = "";

            public Tile(string label)
            {
                _label = label;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                         | ControlStyles.ResizeRedraw, true);
                SetStyle(ControlStyles.Selectable, false);
                TabStop = false;
                AccessibleRole = AccessibleRole.StaticText;
                Width = Ui.S(96);
            }

            public string Value
            {
                get => _value;
                set
                {
                    _value = value ?? "";
                    AccessibleName = _label + ": " + _value;
                    Invalidate();
                }
            }

            public int HeightFor(int width) =>
                Ui.S(Ui.Space2) * 2 + TextRenderer.MeasureText("Ag", Ui.Caption).Height + Ui.S(2) + TextRenderer.MeasureText("Ag", Ui.Heading).Height;

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(Parent?.BackColor ?? Color.White);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var path = Ui.Rounded(new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f), Ui.S(Ui.Radius)))
                using (var fill = new SolidBrush(Ui.Page))
                using (var pen = new Pen(Ui.Border))
                {
                    g.FillPath(fill, path);
                    g.DrawPath(pen, path);
                }
                var pad = Ui.S(Ui.Space2) + Ui.S(2);
                var lh = TextRenderer.MeasureText("Ag", Ui.Caption).Height;
                const TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
                TextRenderer.DrawText(g, _label, Ui.Caption, new Rectangle(pad, Ui.S(Ui.Space2), Width - pad * 2, lh), Ui.Muted, flags);
                TextRenderer.DrawText(g, _value, Ui.Heading, new Rectangle(pad, Ui.S(Ui.Space2) + lh + Ui.S(2), Width - pad * 2, Height - lh), Ui.Ink, flags);
            }
        }

        /// <summary>A thin progress bar in the neutral accent gradient (the job on the printer).</summary>
        private sealed class Meter : Control, IAutoHeight
        {
            private double _value;

            public Meter()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                         | ControlStyles.ResizeRedraw, true);
                SetStyle(ControlStyles.Selectable, false);
                TabStop = false;
                AccessibleRole = AccessibleRole.ProgressBar;
            }

            public double Value
            {
                get => _value;
                set
                {
                    _value = Math.Max(0, Math.Min(1, value));
                    AccessibleName = "Job progress " + Math.Round(_value * 100) + "%";
                    Invalidate();
                }
            }

            public int HeightFor(int width) => Ui.S(6);

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(Parent?.BackColor ?? Color.White);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var r = new RectangleF(0, 0, Width - 1f, Height - 1f);
                using (var track = Ui.Rounded(r, r.Height / 2))
                using (var fill = new SolidBrush(Ui.Subtle))
                    g.FillPath(fill, track);
                var w = (float)(r.Width * _value);
                if (w < 1) return;
                var bar = new RectangleF(0, 0, Math.Max(w, r.Height), r.Height);
                using (var path = Ui.Rounded(bar, r.Height / 2))
                using (var brush = Brand.Gradient(Rectangle.Ceiling(bar)))
                    g.FillPath(brush, path);
            }
        }
    }
}
