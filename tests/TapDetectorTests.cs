using System;
using TwoCtrl;
class TapDetectorTests
{
    static int count;
    static void Equal(int expected, int actual, string scenario)
    {
        count++;
        if (expected != actual) throw new Exception(scenario + ": expected " + expected + ", got " + actual);
    }
    static int Down(TapDetector d, int k) { return d.Process(k, true, false); }
    static int Up(TapDetector d, int k) { return d.Process(k, false, false); }
    static void Main()
    {
        int left = TapDetector.LeftControl, right = TapDetector.RightControl;
        var d = new TapDetector();
        Equal(0, Down(d, left), "switch only on release");
        Equal(left, Up(d, left), "left solo");
        Down(d, right); Equal(right, Up(d, right), "right solo");
        Down(d, left); Down(d, 67); Up(d, 67); Equal(0, Up(d, left), "Ctrl+C");
        Down(d, left); Down(d, 67); Equal(0, Up(d, left), "Ctrl released before C"); Up(d, 67);
        Down(d, 65); Down(d, left); Equal(0, Up(d, left), "key already held"); Up(d, 65);
        Down(d, left); Down(d, right); Equal(0, Up(d, right), "both controls"); Equal(0, Up(d, left), "both released");
        Down(d, left); d.Cancel(); Equal(0, Up(d, left), "mouse or pause cancels");
        Down(d, left); Down(d, left); Equal(left, Up(d, left), "repeat produces one switch"); Equal(0, Up(d, left), "duplicate release");
        Down(d, right); d.Process(65, true, true); Equal(0, Up(d, right), "injected event cancels");
        d.Process(left, true, true); Down(d, 0xA5); Up(d, 0xA5); Equal(0, d.Process(left, false, true), "AltGr synthetic Ctrl");
        Down(d, left); Down(d, 0xA5); Up(d, 0xA5); Equal(0, Up(d, left), "AltGr reported as physical Ctrl");
        d.Seed(left); Equal(0, Up(d, left), "Ctrl held at startup");
        Down(d, left); Equal(left, Up(d, left), "fresh tap after cancellation");
        Console.WriteLine("Passed " + count + " assertions.");
    }
}
