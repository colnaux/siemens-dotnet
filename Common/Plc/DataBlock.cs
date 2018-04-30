﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using Dmc.Siemens.Common.Base;
using Dmc.Siemens.Common.Interfaces;
using Dmc.Siemens.Common.Plc.Interfaces;
using Dmc.Siemens.Portal.Base;

namespace Dmc.Siemens.Common.Plc
{
    public class DataBlock : DataEntity, IBlock
    {

        #region Public Properties

        private bool _IsOptimized;
        public bool IsOptimized
        {
            get
            {
                return this._IsOptimized;
            }
            set
            {
                this.SetProperty(ref this._IsOptimized, value);
            }
        }

        private bool _IsInstance;
        public bool IsInstance
        {
            get
            {
                return this._IsInstance;
            }
            set
            {
                this.SetProperty(ref this._IsInstance, value);
            }
        }

        private bool _IsDataType;
        public bool IsDataType
        {
            get
            {
                return this._IsDataType;
            }
            set
            {
                this.SetProperty(ref this._IsDataType, value);
            }
        }

        private string _InstanceName;
        public string InstanceName
        {
            get
            {
                return this._InstanceName;
            }
            set
            {
                this.SetProperty(ref this._InstanceName, value);
            }
        }

		public BlockType Type => BlockType.DataBlock;

		public override string DataHeader
		{
			get
			{
				return (this.IsOptimized) ? "VAR" : "STRUCT";
			}
		}

		#endregion

		#region Public Methods

		public override IParsableSource ParseSource(TextReader reader)
        {
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.Contains("S7_Optimized_Access"))
                {
                    this.IsOptimized = line.ToUpper().Contains("TRUE");
                    base.ParseSource(reader);
                    break;
                }
                else if (line.Contains("TITLE ="))
                {
                    this.IsOptimized = false;
                    base.ParseSource(reader);
                    break;
                }
            }
			
            return this;

        }

		public override void ReadXml(XmlReader reader)
		{
			throw new NotImplementedException();
		}

		public override void WriteXml(XmlWriter writer)
		{
			throw new NotImplementedException();
		}

		public override void Export(string path)
		{
			throw new NotImplementedException();
		}

		#endregion


	}
}