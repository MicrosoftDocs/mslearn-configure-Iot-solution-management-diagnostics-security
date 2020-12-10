// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

// INSERT using statements below here

namespace CheeseCaveOperator
{
    class Program
    {
        // INSERT variables below here

        // INSERT Main method below here

        // INSERT ReceiveMessagesFromDeviceAsync method below here

        // INSERT InvokeMethod method below here

        // INSERT Device twins section below here
    }

    internal static class ConsoleHelper
    {
        internal static void WriteColorMessage(string text, ConsoleColor clr)
        {
            Console.ForegroundColor = clr;
            Console.WriteLine(text);
            Console.ResetColor();
        }
        internal static void WriteGreenMessage(string text)
        {
            WriteColorMessage(text, ConsoleColor.Green);
        }

        internal static void WriteRedMessage(string text)
        {
            WriteColorMessage(text, ConsoleColor.Red);
        }
    }
}