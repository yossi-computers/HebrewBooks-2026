using System.Runtime.InteropServices;
using System.Text;

namespace HebrewBooks.Infrastructure.Paths;

internal static class VolumeProbe
{
	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GetVolumeInformation(string rootPathName, StringBuilder? volumeNameBuffer, int volumeNameSize, out uint volumeSerialNumber, out uint maximumComponentLength, out uint fileSystemFlags, StringBuilder? fileSystemNameBuffer, int fileSystemNameSize);

	public static bool TryGetSerial(string rootPath, out uint serial)
	{
		serial = 0u;
		try
		{
			uint maximumComponentLength;
			uint fileSystemFlags;
			return GetVolumeInformation(rootPath, null, 0, out serial, out maximumComponentLength, out fileSystemFlags, null, 0);
		}
		catch
		{
			return false;
		}
	}
}
