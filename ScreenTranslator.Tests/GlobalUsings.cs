global using Microsoft.VisualStudio.TestTools.UnitTesting;

// OCR, WPF and the shared config don't mix well with running tests in parallel
[assembly: DoNotParallelize]
