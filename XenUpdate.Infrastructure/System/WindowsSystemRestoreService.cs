using System.Management;
using XenUpdate.Core.Interfaces;

namespace XenUpdate.Infrastructure.System;

/// <summary>
/// Creates Windows System Restore Points via WMI before update installation.
/// </summary>
public sealed class WindowsSystemRestoreService : ISystemRestoreService
{
    /// <inheritdoc />
    public Task<bool> CreateRestorePointAsync(string description)
    {
        return Task.Run(() =>
        {
            try
            {
                var scope = new ManagementScope(@"\\localhost\root\default");
                var managementClass = new ManagementClass(scope, new ManagementPath("SystemRestore"), null);

                var inParams = managementClass.GetMethodParameters("CreateRestorePoint");
                inParams["Description"] = description;
                inParams["RestorePointType"] = 12; // APPLICATION_INSTALL
                inParams["EventType"] = 100;       // BEGIN_SYSTEM_CHANGE

                var result = managementClass.InvokeMethod("CreateRestorePoint", inParams, null);
                var returnValue = Convert.ToInt32(result["ReturnValue"]);
                return returnValue == 0;
            }
            catch (ManagementException)
            {
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        });
    }
}
