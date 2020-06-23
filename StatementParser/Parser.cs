using System;
using System.IO;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.IO.Compression;
using System.Text;

namespace StatementParser
{
	public abstract class Parser
	{
		/// <summary>
		/// Used to determine which type of statement is being parsed, and thus which parser to use.
		/// The key in this dictionary shoud be a line or section of a line which is unique to that particular type of statement
		/// and occures before any data which actually needs to be parsed.
		/// </summary>
		private static Dictionary<byte[], Type> parserTypes = new Dictionary<byte[], Type>()
		{ { Encoding.ASCII.GetBytes(@"(This Statement is a Facsimile - Not an original)Tj"), typeof(ChaseCredit1Parser) },
		  { Encoding.ASCII.GetBytes(@"(payment by the date listed above, you may have to pay a late fee of)Tj"), typeof(ChaseCredit2Parser)},
		  { NavyFedCreditYearEndSummary1Parser.ParserTypeID, typeof(NavyFedCreditYearEndSummary1Parser) } };

		/// <summary>
		/// Parses a single bank statement into a collection of <see cref="Transaction"/>s, using the contents of the statement to determine which parser should be used.
		/// </summary>
		/// <param name="filePath">The file path of a pdf bank statement.</param>
		/// <returns>The list of transactions parsed from the statement, or <see langword="null"/> if the statemnt type could not be identified for parsing.</returns>
		public static IList<Transaction> ParseStatement(string filePath)
		{
			using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
			using (var reader = new BinaryReader(fs, Encoding.ASCII))
			{
				(_, byte[] key) = reader.ReadUntil(parserTypes.Keys);

				if (key == null) { return null; }

				Parser parser = (Parser)Activator.CreateInstance(parserTypes[key]);
				return parser.DoParse(reader);
			}

		}

		/// <summary>
		/// Parses a single bank statement into a collection of <see cref="Transaction"/>s using the specified type of parser.
		/// </summary>
		/// <param name="filePath">The file path of a pdf bank statement.</param>
		/// <returns>The list of transactions parsed from the statement.</returns>
		public static IList<Transaction> ParseStatement<ParserType>(string filePath) where ParserType : Parser, new()
		{
			using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
			using (var reader = new BinaryReader(fs, Encoding.ASCII))
			{
				Parser parser = Activator.CreateInstance<ParserType>();
				return parser.DoParse(reader);
			}

		}

		/// <summary>
		/// When overriden in a derived class, performs the actual parsing of the statement, after the correct parser type has been established.
		/// </summary>
		/// <returns>The list of transactions parsed from the statement.</returns>
		protected abstract IList<Transaction> DoParse(BinaryReader reader);

		protected static BinaryReader DecompressToReader(byte[] input)
		{
			byte[] cutinput = new byte[input.Length - 2];
			Array.Copy(input, 2, cutinput, 0, cutinput.Length);

			var compressedStream = new MemoryStream(cutinput);
			var decompressor = new DeflateStream(compressedStream, CompressionMode.Decompress);
			var reader = new BinaryReader(decompressor, Encoding.ASCII);

			return reader;
			//{
			//	decompressor.CopyTo(stream);
			//	return stream.ToArray();
			//}			
		}

		protected static byte[] DecompressToBytes(byte[] input)
		{
			byte[] cutinput = new byte[input.Length - 2];
			Array.Copy(input, 2, cutinput, 0, cutinput.Length);

			using (var compressedStream = new MemoryStream(cutinput))
			using (var decompressor = new DeflateStream(compressedStream, CompressionMode.Decompress))
			using (var memory = new MemoryStream())
			{
				decompressor.CopyTo(memory);
				return memory.ToArray();
			}
		}
	}

	public static class BinaryReaderExtentions
	{
		/// <summary>
		/// Moves through <paramref name="reader"/> one byte at a time, until the most recent bytes equal value of <paramref name="untilValue"/> as encoded using <see cref="Encoding.ASCII"/>.
		/// </summary>
		/// <param name="reader">The binary stream in which to search for the value.</param>
		/// <param name="untilValue">A text value to search for within in the binary stream.</param>
		/// <returns>An array of all the bytes consumed while searching for <paramref name="untilValue"/> (not including <paramref name="untilValue"/> itself), or <see langword="null"/> if the end of the stream was reached with no matches.</returns>
		public static byte[] ReadUntil(this BinaryReader reader, string untilValue)
		{
			return ReadUntil(reader, Encoding.ASCII.GetBytes(untilValue));
		}

		/// <summary>
		/// Moves through <paramref name="reader"/> one byte at a time, until the most recent bytes match those of <paramref name="untilValue"/>.
		/// </summary>
		/// <param name="reader">The binary stream in which to search for the byte pattern.</param>
		/// <param name="untilValue">A byte array to search for search for within in the binary stream.</param>
		/// <returns>An array of all the bytes consumed while searching for <paramref name="untilValue"/> (not including <paramref name="untilValue"/> itself), or <see langword="null"/> if the end of the stream was reached with no matches.</returns>
		public static byte[] ReadUntil(this BinaryReader reader, byte[] untilValue)
		{
			var bytes = new List<byte>();
			var lastBytes = new Queue<byte>();

			do
			{
				int b;

				try
				{
					b = reader.ReadByte();
				}
				catch (EndOfStreamException ex)
				{
					b = -1;
				}

				if (b == -1)
				{
					return null;
				}

				bytes.Add((byte)b);

				lastBytes.Enqueue((byte)b);
				if (lastBytes.Count > untilValue.Length) { lastBytes.Dequeue(); }
			} while (!Enumerable.SequenceEqual(lastBytes, untilValue));

			var r = new byte[bytes.Count - untilValue.Length];
			Array.Copy(bytes.ToArray(), r, r.Length);
			return r;
		}

		/// <summary>
		///  Moves through <paramref name="reader"/> one byte at a time until the most recent bytes match one of the values of <paramref name="untilValues"/>.
		/// </summary>
		/// <param name="reader">The binary stream in which to search for one of the values</param>
		/// <param name="untilValues">A collection of values to search for within the lines of the binary stream.</param>
		/// <returns>The single byte array out of <paramref name="untilValues"/> which was matched first in the binary stream, or <see langword="null"/> if the end of the stream was reached with no matches.</returns>
		public static (byte[] ConsumedBytes, byte[] MatchedValue) ReadUntil(this BinaryReader reader, IEnumerable<byte[]> untilValues)
		{
			if (untilValues.Any(v => v.Length == 0)) { throw new ArgumentException("Empty values are not allowed"); }

			var bytes = new List<byte>();
			byte[] matchedValue = null;

			for (; ; )
			{
				int b;

				try
				{
					b = reader.ReadByte();
				}
				catch (EndOfStreamException ex)
				{
					b = -1;
				}

				if (b == -1) { break; }

				bytes.Add((byte)b);

				var possibleMatches = new List<byte[]>(untilValues);
				int i = 0;

				do
				{
					int bytesIndex = (bytes.Count - 1) - i;
					if (bytesIndex < 0) { break; }

					var possibleMatchesTemp = new List<byte[]>(possibleMatches);

					foreach (var v in possibleMatchesTemp)
					{
						int vindex = (v.Length - 1) - i;
						if (vindex < 0)
						{
							matchedValue = v;
							goto breakout;
						}

						if (bytes[bytesIndex] != v[vindex]) { possibleMatches.Remove(v); }
					}

					i++;
				} while (possibleMatches.Count > 0);
			}

breakout:   byte[] r;

			if (matchedValue == null)
			{
				r = bytes.ToArray();
			}
			else
			{
				r = new byte[bytes.Count - matchedValue.Length];
				Array.Copy(bytes.ToArray(), r, r.Length);
			}

			return (r, matchedValue);
		}

		/// <summary>
		/// Reads through <paramref name="reader"/> until a line-ending marker ('\r', '\n' or "\r\n") is encountered.
		/// </summary>
		/// <param name="reader">The stream to read through</param>
		/// <returns>A <see cref="Encoding.ASCII"/> string created from the bytes read (not encluing the line-ending character(s)).</returns>
		public static string ReadLine(this BinaryReader reader)
		{
			var sb = new StringBuilder();
			char c = reader.ReadChar();

			while (c != '\r' && c != '\n')
			{
				sb.Append(c);
				c = reader.ReadChar();
			}

			//Check for \r\n and consume next char if so
			if (c == '\r' && reader.PeekChar() == '\n') { reader.ReadChar(); }

			return sb.ToString();
		}
	}

	public abstract class ChaseCreditParser : Parser
	{
		protected string fullDateFormat { get; set; } = @"MM/dd/yy";
		
		protected virtual (DateTime startDate, DateTime endDate, Dictionary<int, int> yearMap) FindDateRange(BinaryReader reader)
		{
			//"(Opening/Closing Date)Tj" comes just before the date range
			reader.ReadUntil(@"(Opening/Closing Date)Tj");

			//Skip the remainder of the current line, and the following "Tm" line
			reader.ReadLine();
			reader.ReadLine();

			string dateRange = reader.ReadLine();
			string dateSplitter = @" - ";
			int dateSplitterIndex = dateRange.IndexOf(dateSplitter);

			string startDateString = dateRange.Substring(1, dateSplitterIndex - 1);
			DateTime startDate = DateTime.ParseExact(startDateString, fullDateFormat, CultureInfo.CurrentCulture);

			string endDateString = dateRange.Substring(dateSplitterIndex + dateSplitter.Length, (dateRange.Length - 3) - (dateSplitterIndex + dateSplitter.Length));
			DateTime endDate = DateTime.ParseExact(endDateString, fullDateFormat, CultureInfo.CurrentCulture);

			//Dictionary<month, year>
			var yearMap = new Dictionary<int, int>();
			yearMap.Add(startDate.Month, startDate.Year);
			yearMap.Add(endDate.Month, endDate.Year);

			return (startDate, endDate, yearMap);
		}
	}

	public class ChaseCredit1Parser : ChaseCreditParser
	{		
		//Only tested with two tables, didn't have any statements with more. Any middle table (one's that aren't first or last) will probably have different "ends".
		private string[] tableEnds = { @"(CARDMEMBER SERVICE)Tj", @"(Total fees charged in " };

		protected override IList<Transaction> DoParse(BinaryReader reader)
		{
			var transactions = new List<Transaction>();
			string line;

			//Get statement date range
			(var startDate, var endDate, var yearMap) = FindDateRange(reader);			

			//Keep looping through each transaction table (seperate tables for seperate pages).
			//Each starts with "($ Amount)Tj", just like the first
			//The first and last tables end differently
			while (reader.ReadUntil(@"($ Amount)Tj") != null)
			{
				//Skip the remainder of the current line and the first "Tm" line, then get the first actual line of the transaction
				reader.ReadLine();
				reader.ReadLine();
				line = reader.ReadLine();

				//Loop until one of the end-markers for the table is reached
				while (!tableEnds.Any(e => line.Contains(e)))
				{
					var trans = new Transaction();

					//Transactions are spread over three lines: date, description and then amount.
					//Each useful line is preceeded by a "Tm" line.
					//Each useful line starts with "(" and ends with ")Tj"

					//The first line will have the date:
					//(mm/dd)Tj
					int month = int.Parse(line.Substring(1, 2));
					int day = int.Parse(line.Substring(4, 2));
					trans.Date = new DateTime(yearMap[month], month, day);

					//Skip "Tm" line, then read next
					reader.ReadLine();
					line = reader.ReadLine();

					trans.Description = line.Substring(1, line.Length - 4);

					//Skip "Tm" line, then read next
					reader.ReadLine();
					line = reader.ReadLine();

					trans.Amount = decimal.Parse(line.Substring(1, line.Length - 4));

					transactions.Add(trans);

					//Move to the next real line of the next transaction before looping
					reader.ReadLine();
					line = reader.ReadLine();
				}
			}

			return transactions;
		}
	}

	public class ChaseCredit2Parser : ChaseCreditParser
	{
		//Only tested with a single table, didn't have any statements with more.
		private string[] tableEnds = { @"(Total fees charged in " };

		protected override IList<Transaction> DoParse(BinaryReader reader)
		{
			var transactions = new List<Transaction>();
			string line;

			//Get statement date range
			(var startDate, var endDate, var yearMap) = FindDateRange(reader);

			//Assuming all tables start with "($ Amount)Tj" (like the other chase statements), but I've only tested with statements containing one table
			while (reader.ReadUntil(@"($ Amount)Tj") != null)
			{
				//Skip the remainder of the current line and the first "Tm" line, then get the first actual line of the transaction
				reader.ReadLine();
				reader.ReadLine();
				line = reader.ReadLine();

				//Loop until one of the end-markers for the table is reached
				while (!tableEnds.Any(e => line.Contains(e)))
				{
					var trans = new Transaction();

					//Transactions are spread over three lines: date, description and then amount.
					//Each useful line is preceeded by a "Tm" line.
					//Each useful line starts with "(" and ends with ")Tj"
					//The description line is also preceeded by a line of "[( )] TJ", which itself is proceeded by a "Tm" line.

					//The first line will have the date:
					//(mm/dd)Tj
					int month = int.Parse(line.Substring(1, 2));
					int day = int.Parse(line.Substring(4, 2));
					trans.Date = new DateTime(yearMap[month], month, day);

					//Skip "Tm" line, the "[( )] TJ" line, another "Tm" line, then read next
					reader.ReadLine();
					reader.ReadLine();
					reader.ReadLine();
					line = reader.ReadLine();

					trans.Description = line.Substring(1, line.Length - 4);

					//Skip "Tm" line, then read next
					reader.ReadLine();
					line = reader.ReadLine();

					trans.Amount = decimal.Parse(line.Substring(1, line.Length - 4));

					transactions.Add(trans);

					//Move to the next real line of the next transaction before looping
					reader.ReadLine();
					line = reader.ReadLine();
				}
			}

			return transactions;
		}
	}

	public class NavyFedCreditYearEndSummary1Parser : Parser
	{
		protected const string dateFormat = @"MM/dd/yy";

		public static byte[] ParserTypeID
		{
			get
			{
				var r = new List<byte>();
				r.AddRange(Encoding.ASCII.GetBytes(" obj\n<<\n/Length 8\n/Filter [/FlateDecode]\n>>\nstream\n"));
				r.AddRange(new byte[] { 120, 156, 3, 0, 0, 0, 0, 1 });
				r.AddRange(Encoding.ASCII.GetBytes("\nendstream\nendobj"));
				return r.ToArray();
			}
		}

		protected byte[] GetNextXPosition(BinaryReader reader)
		{
			byte[] x = null;

			//"Moving after Tf\n" brings us to the start of the "Tm" line.
			//The "Tm" line gives positioning information
			reader.ReadUntil(" Tf\n");

			//The "Tm" line has a few numbers, seperated by spaces (" "), we only need the 5th one, which gives the X offset.
			//So we read the first 5 number on the line, only keeping the last.
			for (int i = 0; i < 5; i++)
			{
				x = reader.ReadUntil(" ");
			}

			//We don't need the actual x offset in readable units, we just need to be able to compare for an exact match, the the raw bytes will do.
			return x;
		}

		protected override IList<Transaction> DoParse(BinaryReader reader)
		{
			var transactions = new List<Transaction>();

			//Skip the first 8 pages to get to the transaction detail
			for (int i = 1; i <= 7; i++)
			{
				reader.ReadUntil("\n/Type /Page\n");
			}

			//Skip the 8th page at start of the loop
			while (reader.ReadUntil("\n/Type /Page\n") != null)
			{
				//The actual data is stuffed inside a compressed "stream".
				//We need to read the compressed binary data, decompress it, then move through the decompressed data.
				reader.ReadUntil("\nstream");
				reader.ReadLine();
				var compressedStreamContent = reader.ReadUntil("\nendstream");

				using (var reader2 = DecompressToReader(compressedStreamContent))
				{
					//The data for each table starts a header, which includes "(Post Date) Tj"
					while (reader2.ReadUntil("(Post Date) Tj") != null)
					{
						//We need to get the X position for the "Description" column.
						//We'll need that for when there are multi-line descriptions, since each line is its own, seperate element.
						//The next field after "(Post Date) Tj" is "Description".
						//This function will get the X position of that field.
						var descriptionColumnPosition = GetNextXPosition(reader2);

						//Jump to the end of header row
						reader2.ReadUntil("(Credits) Tj");

						byte[] raw;
						string text;

						//"Tm\n(" marks the begining of new field
						//"\n0.0000 0.0000 0.0000" marks the end of the table.
						//If the latter is found first, then we need to skip ahead to the next table on the page (if any)
						var lookingFor = new byte[][] { Encoding.ASCII.GetBytes("Tm\n("),
													Encoding.ASCII.GetBytes("\n0.0000 0.0000 0.0000") };

						while (reader2.ReadUntil(lookingFor).MatchedValue == lookingFor[0])
						{
							var transaction = new Transaction();

							//Each data field is enclosed in an "BT ... ET" block comprised of four lines.
							//The block includes font and position data.
							//The data itself is on the third line.
							//The second lines end in "Tm"
							//The data on the third line is preceeded by "(" and proceeded by ") Tj"

							//The order of the data fields is date, description and then amount.
							//The description can be more than one line, each line is a seperate data field block, but the fields are contiguous.

							//Reader will start at after the opening "(" for the date field, just before the actual date text.
							//Read untill the end of the text, marked by ") Tj".
							raw = reader2.ReadUntil(") Tj");
							text = Encoding.ASCII.GetString(raw);
							transaction.Date = DateTime.ParseExact(text, dateFormat, CultureInfo.CurrentCulture);

							//Move to the next field (the description field) and repeat.
							reader2.ReadUntil("Tm\n(");
							raw = reader2.ReadUntil(") Tj");
							text = Encoding.ASCII.GetString(raw);
							transaction.Description = text;

							//The next few fields might be more description lines
							//We need to compare the X position of each one until it no longer matches the X position of the "Description" header.
							while (Enumerable.SequenceEqual(GetNextXPosition(reader2), descriptionColumnPosition))
							{
								//Read each additional description line and append it to the existing description
								reader2.ReadUntil("Tm\n(");
								raw = reader2.ReadUntil(") Tj");
								text = Encoding.ASCII.GetString(raw);
								transaction.Description = transaction.Description + Environment.NewLine + text;
							}

							//Then, finally, the amount field
							reader2.ReadUntil("Tm\n(");
							raw = reader2.ReadUntil(") Tj");
							text = Encoding.ASCII.GetString(raw);
							transaction.Amount = decimal.Parse(text);

							transactions.Add(transaction);
						}
					}
				}
			}

			return transactions;
		}
	}

	public struct Transaction
	{
		public Transaction(DateTime date, string description, decimal amount)
		{
			Date = date;
			Description = description;
			Amount = amount;
		}

		public DateTime Date { get; set; }
		public string Description { get; set; }
		public decimal Amount { get; set; }
	}
}