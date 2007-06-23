using System;
using System.IO;
using System.Web;
using System.Reflection;
using System.Text;
using System.Web.Compilation;

namespace JsonFx.Handlers
{
	public abstract class ResourceHandler : IHttpHandler
	{
		#region Constants

		private const int BufferSize = 1024;

		#endregion Constants

		#region Properties

		protected abstract string ResourceContentType { get; }
		protected abstract string ResourceExtension { get; }

		#endregion Properties

		#region IHttpHandler Members

		void IHttpHandler.ProcessRequest(HttpContext context)
		{
			bool isDebug = "debug".Equals(context.Request.QueryString[null], StringComparison.InvariantCultureIgnoreCase);

			context.Response.ClearHeaders();
			context.Response.BufferOutput = true;
			context.Response.ContentEncoding = System.Text.Encoding.UTF8;
			context.Response.ContentType = this.ResourceContentType;

			context.Response.AppendHeader(
				"Content-Disposition",
				"inline;filename="+Path.GetFileNameWithoutExtension(context.Request.FilePath)+this.ResourceExtension);

			if (isDebug)
			{
				context.Response.Cache.SetCacheability(HttpCacheability.NoCache);
			}

			// specifying "DEBUG" in the query string gets the non-compacted form
			Stream input = this.GetResourceStream(context, isDebug);
			if (input == null)
			{
				//throw new HttpException((int)System.Net.HttpStatusCode.NotFound, "Invalid path");
				this.OutputTargetFile(context);
			}
			else if (input != Stream.Null)
			{
				this.BufferedWrite(context, input);
			}

			// this safely ends request without causing "Transfer-Encoding: Chunked" which chokes IE6
			context.ApplicationInstance.CompleteRequest();
		}

		bool IHttpHandler.IsReusable
		{
			get { return true; }
		}

		#endregion IHttpHandler Members

		#region ResourceHandler Members

		/// <summary>
		/// Determines the appropriate source stream for the incomming request
		/// </summary>
		/// <param name="context"></param>
		/// <param name="isDebug"></param>
		/// <returns></returns>
		protected virtual Stream GetResourceStream(HttpContext context, bool isDebug)
		{
			string virtualPath = context.Request.AppRelativeCurrentExecutionFilePath;
			ResourceHandlerInfo info = ResourceHandlerInfo.GetHandlerInfo(virtualPath);
			if (info == null)
			{
				return null;
			}
			string resourcePath = isDebug ? info.ResourceName : info.CompactResourceName;

			Assembly assembly = BuildManager.GetCompiledAssembly(virtualPath);

			// check if client has cached copy
			EmbeddedResourceETag eTag = new EmbeddedResourceETag(assembly, resourcePath);
			if (eTag.HandleETag(context))
			{
				return Stream.Null;
			}

			return assembly.GetManifestResourceStream(resourcePath);
		}

		protected void OutputTargetFile(HttpContext context)
		{
			string fileName = context.Request.PhysicalPath;

			// check if client has cached copy
			FileETag eTag = new FileETag(fileName);
			if (!eTag.HandleETag(context))
			{
				context.Response.TransmitFile(fileName);

				//using (StreamReader reader = File.OpenText(fileName))
				//{
				//    this.BufferedWrite(context, reader);
				//}
			}
		}

		protected void BufferedWrite(HttpContext context, Stream input)
		{
			if (input == null)
			{
				throw new HttpException((int)System.Net.HttpStatusCode.NotFound, "Input stream is null.");
			}
			using (TextReader reader = new StreamReader(input, System.Text.Encoding.UTF8))
			{
				TextWriter writer = context.Response.Output;
				// buffered write to response
				char[] buffer = new char[ResourceHandler.BufferSize];
				int count;
				do
				{
					count = reader.ReadBlock(buffer, 0, ResourceHandler.BufferSize);
					writer.Write(buffer, 0, count);
				} while (count > 0);

				// flushing/closing the output causes "Transfer-Encoding: Chunked" which chokes IE6
			}
		}

		#endregion ResourceHandler Members
	}
}