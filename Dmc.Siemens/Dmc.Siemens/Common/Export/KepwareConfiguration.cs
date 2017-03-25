﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Dmc.IO;
using Dmc.Siemens.Base;
using Dmc.Siemens.Common.Base;
using Dmc.Siemens.Common.Interfaces;
using Dmc.Siemens.Common.PLC;
using Dmc.Siemens.Common.PLC.Interfaces;
using Dmc.Siemens.Portal.Base;

namespace Dmc.Siemens.Common.Export
{
	public static class KepwareConfiguration
	{

		#region Public Methods

		public static void CreateFromBlocks(IEnumerable<IBlock> blocks, string path, IPlc owningPlc)
		{
			if (blocks == null)
				throw new ArgumentNullException(nameof(blocks));
			IEnumerable<DataBlock> dataBlocks;
			if ((dataBlocks = blocks.OfType<DataBlock>())?.Count() <= 0)
				throw new ArgumentException("Blocks does not contain any valid DataBlocks.", nameof(blocks));

			CreateFromBlocksInternal(dataBlocks, path, owningPlc);
		}

		public static void CreateFromBlocks(DataBlock block, string path, IPlc owningPlc)
		{
			CreateFromBlocksInternal(new[] { block }, path, owningPlc);
		}

		#endregion

		#region Private Methods

		private static void CreateFromBlocksInternal(IEnumerable<DataBlock> blocks, string path, IPlc parentPlc)
		{
			if (path == null)
				throw new ArgumentNullException(nameof(path));
			if (!FileHelpers.CheckValidFilePath(path, ".csv"))
				throw new ArgumentException(path + " is not a valid path.", nameof(path));

			try
			{
				using (var file = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.Read))
				{
					StreamWriter writer = new StreamWriter(file);

					WriteHeaders(writer);

					foreach (var block in blocks)
					{
						if (block == null)
							throw new ArgumentNullException(nameof(block));
						if (block.Children?.Count <= 0)
							throw new ArgumentException("Block '" + block.Name + "' contains no data", nameof(block));

						ExportDataBlockToFile(block, writer, parentPlc);
					}
				}
			}
			catch (Exception e)
			{
				throw new SiemensException("Could not write Kepware configuration", e);
			}
		}

		private static void ExportDataBlockToFile(DataBlock block, TextWriter writer, IPlc parentPlc)
		{
			Address currentAddress = new Address();
			foreach (var entry in block)
			{
				AddDataEntry(entry, block.Name, currentAddress);
				currentAddress += entry.CalculateSize(parentPlc);
				if (entry.DataType == DataType.UDT || entry.DataType == DataType.STRUCT || entry.DataType == DataType.ARRAY)
					currentAddress = TagHelper.IncrementAddress(currentAddress);
			}

			void AddDataEntry(IDataEntry entry, string entryPrefix, Address address)
			{
				string addressPrefix = "";
				string type = "";

				DataEntry dataEntry = (entry as DataEntry);
				// Check to make sure if it is not a DataEntry then it is a STRUCT
				if (dataEntry == null && entry.DataType != DataType.STRUCT)
					throw new SiemensException("Cannot have a non-DataEntry IDataEntry that is not a STRUCT");

				switch (entry.DataType)
				{
					case DataType.ARRAY:
						address = TagHelper.IncrementAddress(address);
						DataType subType = dataEntry.ArrayDataEntry.HasValue ? dataEntry.ArrayDataEntry.Value : DataType.UDT;
						bool isNonPrimitive = !TagHelper.IsPrimitive(subType);
						int primitiveByteSize = TagHelper.GetPrimitiveByteSize(subType);
						IDataEntry structContents = null;
						if (isNonPrimitive)
						{
							switch (subType)
							{
								case DataType.ARRAY:
									throw new SiemensException("2D arrays not supported: " + entry.Name);
								case DataType.STRUCT:
									structContents = entry;
									break;
								default:
									try
									{
										structContents = parentPlc.GetUdtStructure(dataEntry.DataTypeName);
										break;
									}
									catch (Exception e)
									{
										throw new SiemensException("Unsupported array type of '" + entry.Name + "', type: " + dataEntry.DataTypeName, e);
									}
							}

							for (int i = parentPlc.GetConstantValue(dataEntry.ArrayStartIndex); i <= parentPlc.GetConstantValue(dataEntry.ArrayEndIndex); i++)
							{
								AddDataEntry(structContents, $"{entryPrefix}{entry.Name}[{i}].", address);
							}
						}
						else // if it is a primitive, create a temporary data entry to make recurison/address generation simpler
						{
							LinkedList<DataEntry> arrayEntries = new LinkedList<DataEntry>();
							for (int i = parentPlc.GetConstantValue(dataEntry.ArrayStartIndex); i <= parentPlc.GetConstantValue(dataEntry.ArrayEndIndex); i++)
							{
								arrayEntries.AddLast(new DataEntry($"{entry.Name}[{i}]", subType, entry.Comment + " (" + i.ToString() + ")", stringLength: dataEntry.StringLength));
							}

							structContents = new DataEntry(entry.Name, DataType.STRUCT, entry.Comment, arrayEntries);

							AddDataEntry(structContents, entryPrefix, address);
						}
						break;
					case DataType.BOOL:
						addressPrefix = "DBX";
						type = "Boolean";
						break;
					case DataType.BYTE:
						addressPrefix = "DBB";
						type = "Byte";
						break;
					case DataType.CHAR:
						addressPrefix = "DBB";
						type = "Char";
						break;
					case DataType.DATE:
					case DataType.DATE_AND_TIME:
						addressPrefix = "DATE";
						type = "Date";
						break;
					case DataType.TIME:
					case DataType.DINT:
						addressPrefix = "DBD";
						type = "Long";
						break;
					case DataType.DWORD:
						addressPrefix = "DBD";
						type = "Dword";
						break;
					case DataType.INT:
						addressPrefix = "DBW";
						type = "Short";
						break;
					case DataType.REAL:
						addressPrefix = "DBD";
						type = "Float";
						break;
					case DataType.STRING:
						addressPrefix = "STRING";
						type = "String";
						break;
					case DataType.UDT:
						structContents = parentPlc.GetUdtStructure(dataEntry.DataTypeName);
						dataEntry.SetUdtStructure(structContents.Children);
						AddStruct(dataEntry);
						break;
					case DataType.STRUCT:
						AddStruct(entry);
						break;
					case DataType.WORD:
						addressPrefix = "DBW";
						type = "Word";
						break;
					default:
						throw new SiemensException("Data type: '" + entry.DataType.ToString() + "' not supported.");
				}

				// sub function to deal with adding a struct
				void AddStruct(IDataEntry structEntry)
				{
					var addresses = structEntry.CalcluateAddresses(parentPlc);
					
					if (!structEntry.Children.First.Value.Name.Contains("["))
					{
						entryPrefix += "." + structEntry.Name + ".";
					}

					foreach (var subEntry in structEntry.Children)
					{
						AddDataEntry(subEntry, entryPrefix, addresses[subEntry] + address);
					}
				}

				if (TagHelper.IsPrimitive(entry.DataType))
				{
					string addressString = "DB" + block.Number + "." + addressPrefix;
					if (entry.DataType == DataType.BOOL)
						addressString += address.Byte + "." + address.Bit;
					else if (entry.DataType == DataType.STRING)
						addressString += address.Byte + "." + parentPlc.GetConstantValue(dataEntry.StringLength).ToString();
					else
						addressString += address.Byte;

					string[] entryItems = new string[16]
					{
					entryPrefix + entry.Name,
					addressString,
					type,
					"1",
					"R/W",
					"100",
					string.Empty,
					string.Empty,
					string.Empty,
					string.Empty,
					string.Empty,
					string.Empty,
					string.Empty,
					string.Empty,
					string.Empty,
					entry.Comment
					};

					writer.WriteLine(string.Join(",", entryItems));
				}

			}



		}

		private static void WriteHeaders(TextWriter writer)
		{
			writer.WriteLine("Tag Name,Address,Data Type,Respect Data Type,Client Access,Scan Rate,Scaling,Raw Low,Raw High,Scaled Low,Scaled High,Scaled Data Type,Clamp Low,Clamp High,Eng Units,Description");
		}

		#endregion

	}
}