#include <ntddk.h>
#include <wdf.h>
#include <vhf.h>
#include "report_descriptor.h"

#define IOCTL_PTP_SUBMIT CTL_CODE(FILE_DEVICE_UNKNOWN, 0x800, METHOD_BUFFERED, FILE_WRITE_DATA)

typedef struct {
    VHFHANDLE Vhf;
    WDFSPINLOCK Lock;
    WDFTIMER Timer;
    PTP_REPORT Last;
    LONGLONG LastArrival;
    UCHAR Mode;
    UCHAR Switches;
    BOOLEAN AwaitAllUp;
    ULONG Diagnostics[24];
} DEVICE_CONTEXT;
WDF_DECLARE_CONTEXT_TYPE_WITH_NAME(DEVICE_CONTEXT, Context)

DRIVER_INITIALIZE DriverEntry;
EVT_WDF_DRIVER_DEVICE_ADD DeviceAdd;
EVT_WDF_OBJECT_CONTEXT_CLEANUP Cleanup;
EVT_WDF_IO_QUEUE_IO_DEVICE_CONTROL Ioctl;
EVT_WDF_TIMER Watchdog;
EVT_WDF_FILE_CLEANUP FileCleanup;
EVT_VHF_ASYNC_OPERATION GetFeature;
EVT_VHF_ASYNC_OPERATION SetFeature;
EVT_WDF_DEVICE_D0_ENTRY D0Entry;
EVT_WDF_DEVICE_D0_EXIT D0Exit;

static NTSTATUS Submit(DEVICE_CONTEXT *ctx, PTP_REPORT *report) {
    HID_XFER_PACKET packet;
    packet.reportId = 1;
    packet.reportBuffer = (PUCHAR)report;
    packet.reportBufferLen = sizeof(*report);
    return VhfReadReportSubmit(ctx->Vhf, &packet);
}

/* Caller holds the lock. Never leave fingers held after disconnect or a stall. */
static BOOLEAN ReleaseContacts(DEVICE_CONTEXT *ctx) {
    UCHAR i;
    BOOLEAN hasDown = FALSE;
    for (i = 0; i < ctx->Last.ContactCount; ++i) {
        if (ctx->Last.Contacts[i].Flags & 2) hasDown = TRUE;
        ctx->Last.Contacts[i].Flags &= 1;
    }
    if (hasDown && ctx->Vhf) {
        ctx->Last.ScanTime = (USHORT)(KeQueryInterruptTime() / 1000);
        (void)Submit(ctx, &ctx->Last);
    }
    RtlZeroMemory(&ctx->Last, sizeof(ctx->Last));
    ctx->Last.ReportId = 1;
    return hasDown;
}

void Watchdog(WDFTIMER timer) {
    DEVICE_CONTEXT *ctx = Context(WdfTimerGetParentObject(timer));
    WdfSpinLockAcquire(ctx->Lock);
    if ((LONGLONG)KeQueryInterruptTime() - ctx->LastArrival > 5000000) {
        if (ReleaseContacts(ctx)) ctx->AwaitAllUp = TRUE;
    }
    WdfSpinLockRelease(ctx->Lock);
}

void FileCleanup(WDFFILEOBJECT file) {
    DEVICE_CONTEXT *ctx = Context(WdfFileObjectGetDevice(file));
    WdfSpinLockAcquire(ctx->Lock);
    ReleaseContacts(ctx);
    ctx->AwaitAllUp = TRUE;
    WdfSpinLockRelease(ctx->Lock);
}

void GetFeature(PVOID context, VHFOPERATIONHANDLE operation, PVOID unused, PHID_XFER_PACKET packet) {
    DEVICE_CONTEXT *ctx = context;
    NTSTATUS status = STATUS_SUCCESS;
    UCHAR id = packet->reportId;
    ULONG required = id == 2 ? 3 : id == 3 ? 257 : id == 4 || id == 5 ? 2 : 0;
    UNREFERENCED_PARAMETER(unused);
    WdfSpinLockAcquire(ctx->Lock);
    if (id < 6) ctx->Diagnostics[8 + id]++;
    ctx->Diagnostics[20] = id;
    ctx->Diagnostics[21] = packet->reportBufferLen;
    WdfSpinLockRelease(ctx->Lock);
    if (!required) status = STATUS_NOT_SUPPORTED;
    else if (packet->reportBufferLen < required) status = STATUS_BUFFER_TOO_SMALL;
    else {
        RtlZeroMemory(packet->reportBuffer, required);
        packet->reportBuffer[0] = id;
        WdfSpinLockAcquire(ctx->Lock);
        if (id == 2) { packet->reportBuffer[1] = 5; packet->reportBuffer[2] = 1; }
        if (id == 4) packet->reportBuffer[1] = ctx->Mode;
        if (id == 5) packet->reportBuffer[1] = ctx->Switches;
        WdfSpinLockRelease(ctx->Lock);
    }
    VhfAsyncOperationComplete(operation, status);
}

void SetFeature(PVOID context, VHFOPERATIONHANDLE operation, PVOID unused, PHID_XFER_PACKET packet) {
    DEVICE_CONTEXT *ctx = context;
    NTSTATUS status = STATUS_SUCCESS;
    UCHAR id = packet->reportId;
    UNREFERENCED_PARAMETER(unused);
    WdfSpinLockAcquire(ctx->Lock);
    if (id < 6) ctx->Diagnostics[14 + id]++;
    ctx->Diagnostics[22] = id;
    ctx->Diagnostics[23] = packet->reportBufferLen;
    WdfSpinLockRelease(ctx->Lock);
    if (id != 4 && id != 5) status = STATUS_NOT_SUPPORTED;
    else if (packet->reportBufferLen < 2) status = STATUS_BUFFER_TOO_SMALL;
    else if ((id == 4 && packet->reportBuffer[1] != 0 && packet->reportBuffer[1] != 3) ||
             (id == 5 && packet->reportBuffer[1] > 3)) status = STATUS_INVALID_PARAMETER;
    else {
        WdfSpinLockAcquire(ctx->Lock);
        ReleaseContacts(ctx);
        ctx->AwaitAllUp = TRUE;
        if (id == 4) ctx->Mode = packet->reportBuffer[1];
        else ctx->Switches = packet->reportBuffer[1];
        WdfSpinLockRelease(ctx->Lock);
    }
    VhfAsyncOperationComplete(operation, status);
}

void Ioctl(WDFQUEUE queue, WDFREQUEST request, size_t outSize, size_t inSize, ULONG code) {
    DEVICE_CONTEXT *ctx = Context(WdfIoQueueGetDevice(queue));
    PTP_REPORT *input;
    NTSTATUS status;
    UCHAR i, ids = 0;
    BOOLEAN anyDown = FALSE;
    if (code == 0x00226004) {
        PVOID output;
        status = WdfRequestRetrieveOutputBuffer(request, sizeof(ctx->Diagnostics), &output, NULL);
        if (!NT_SUCCESS(status)) { WdfRequestComplete(request, status); return; }
        WdfSpinLockAcquire(ctx->Lock);
        ctx->Diagnostics[0] = 1;
        ctx->Diagnostics[1] = ctx->Mode;
        ctx->Diagnostics[2] = ctx->Switches;
        ctx->Diagnostics[3] = ctx->AwaitAllUp;
        RtlCopyMemory(output, ctx->Diagnostics, sizeof(ctx->Diagnostics));
        WdfSpinLockRelease(ctx->Lock);
        WdfRequestCompleteWithInformation(request, STATUS_SUCCESS, sizeof(ctx->Diagnostics));
        return;
    }
    UNREFERENCED_PARAMETER(outSize);
    if (code != IOCTL_PTP_SUBMIT) { WdfRequestComplete(request, STATUS_INVALID_DEVICE_REQUEST); return; }
    if (inSize != sizeof(PTP_REPORT)) { WdfRequestComplete(request, STATUS_INVALID_BUFFER_SIZE); return; }
    status = WdfRequestRetrieveInputBuffer(request, sizeof(PTP_REPORT), (PVOID*)&input, NULL);
    if (!NT_SUCCESS(status)) { WdfRequestComplete(request, status); return; }
    if (input->ReportId != 1 || input->ContactCount > 5 || input->Reserved != 0) {
        WdfRequestComplete(request, STATUS_INVALID_PARAMETER); return;
    }
    for (i = 0; i < input->ContactCount; ++i) {
        PTP_CONTACT *c = &input->Contacts[i];
        if (c->Id > 4 || c->X > 4095 || c->Y > 4095 || c->Flags > 3 || (ids & (1 << c->Id))) {
            WdfRequestComplete(request, STATUS_INVALID_PARAMETER); return;
        }
        ids |= (UCHAR)(1 << c->Id);
        if (c->Flags & 2) anyDown = TRUE;
    }
    WdfSpinLockAcquire(ctx->Lock);
    ctx->Diagnostics[4]++;
    ctx->LastArrival = (LONGLONG)KeQueryInterruptTime();
    if (!anyDown) ctx->AwaitAllUp = FALSE;
    if (ctx->Vhf && ctx->Mode == 3 && (ctx->Switches & 1) && !ctx->AwaitAllUp) {
        status = Submit(ctx, input);
        ctx->Diagnostics[5]++;
        ctx->Diagnostics[7] = (ULONG)status;
        if (NT_SUCCESS(status)) ctx->Last = *input;
    } else { ctx->Diagnostics[6]++; status = STATUS_SUCCESS; }
    WdfSpinLockRelease(ctx->Lock);
    WdfRequestComplete(request, status);
}

void Cleanup(WDFOBJECT object) {
    DEVICE_CONTEXT *ctx = Context(object);
    if (ctx->Timer) WdfTimerStop(ctx->Timer, TRUE);
    if (ctx->Vhf) { VhfDelete(ctx->Vhf, TRUE); ctx->Vhf = NULL; }
}

NTSTATUS D0Entry(WDFDEVICE device, WDF_POWER_DEVICE_STATE previous) {
    DEVICE_CONTEXT *ctx = Context(device);
    VHF_CONFIG config;
    NTSTATUS status;
    UNREFERENCED_PARAMETER(previous);
    ctx->Mode = 3;
    ctx->Switches = 3;
    ctx->AwaitAllUp = TRUE;
    VHF_CONFIG_INIT(&config, WdfDeviceWdmGetDeviceObject(device), sizeof(PtpDescriptor), (PUCHAR)PtpDescriptor);
    config.VhfClientContext = ctx;
    /* Deliberately no ASUS VID: this is an independent development device. */
    config.VendorID = 0;
    config.ProductID = 0x0056;
    config.VersionNumber = 1;
    config.EvtVhfAsyncOperationGetFeature = GetFeature;
    config.EvtVhfAsyncOperationSetFeature = SetFeature;
    status = VhfCreate(&config, &ctx->Vhf);
    if (!NT_SUCCESS(status)) return status;
    status = VhfStart(ctx->Vhf);
    if (!NT_SUCCESS(status)) { VhfDelete(ctx->Vhf, TRUE); ctx->Vhf = NULL; return status; }
    WdfTimerStart(ctx->Timer, WDF_REL_TIMEOUT_IN_MS(100));
    return STATUS_SUCCESS;
}

NTSTATUS D0Exit(WDFDEVICE device, WDF_POWER_DEVICE_STATE target) {
    DEVICE_CONTEXT *ctx = Context(device);
    VHFHANDLE vhf;
    UNREFERENCED_PARAMETER(target);
    WdfTimerStop(ctx->Timer, TRUE);
    WdfSpinLockAcquire(ctx->Lock);
    ReleaseContacts(ctx);
    vhf = ctx->Vhf;
    ctx->Vhf = NULL;
    WdfSpinLockRelease(ctx->Lock);
    if (vhf) VhfDelete(vhf, TRUE);
    return STATUS_SUCCESS;
}

NTSTATUS DeviceAdd(WDFDRIVER driver, PWDFDEVICE_INIT init) {
    WDFDEVICE device;
    DEVICE_CONTEXT *ctx;
    WDF_OBJECT_ATTRIBUTES attributes;
    WDF_IO_QUEUE_CONFIG queue;
    WDF_FILEOBJECT_CONFIG file;
    WDF_TIMER_CONFIG timer;
    WDF_PNPPOWER_EVENT_CALLBACKS power;
    NTSTATUS status;
    DECLARE_CONST_UNICODE_STRING(name, L"\\Device\\N56PrecisionBridge");
    DECLARE_CONST_UNICODE_STRING(link, L"\\DosDevices\\N56PrecisionBridge");
    DECLARE_CONST_UNICODE_STRING(sddl, L"D:P(A;;GA;;;SY)(A;;GA;;;BA)");
    UNREFERENCED_PARAMETER(driver);
    WdfDeviceInitSetDeviceType(init, FILE_DEVICE_UNKNOWN);
    WdfDeviceInitSetCharacteristics(init, FILE_DEVICE_SECURE_OPEN, TRUE);
    WdfDeviceInitSetExclusive(init, TRUE);
    status = WdfDeviceInitAssignName(init, &name);
    if (!NT_SUCCESS(status)) return status;
    status = WdfDeviceInitAssignSDDLString(init, &sddl);
    if (!NT_SUCCESS(status)) return status;
    WDF_FILEOBJECT_CONFIG_INIT(&file, WDF_NO_EVENT_CALLBACK, WDF_NO_EVENT_CALLBACK, FileCleanup);
    WdfDeviceInitSetFileObjectConfig(init, &file, WDF_NO_OBJECT_ATTRIBUTES);
    WDF_PNPPOWER_EVENT_CALLBACKS_INIT(&power);
    power.EvtDeviceD0Entry = D0Entry;
    power.EvtDeviceD0Exit = D0Exit;
    WdfDeviceInitSetPnpPowerEventCallbacks(init, &power);
    WDF_OBJECT_ATTRIBUTES_INIT_CONTEXT_TYPE(&attributes, DEVICE_CONTEXT);
    attributes.EvtCleanupCallback = Cleanup;
    attributes.ExecutionLevel = WdfExecutionLevelPassive;
    status = WdfDeviceCreate(&init, &attributes, &device);
    if (!NT_SUCCESS(status)) return status;
    ctx = Context(device);
    ctx->Last.ReportId = 1;
    WDF_OBJECT_ATTRIBUTES_INIT(&attributes);
    attributes.ParentObject = device;
    status = WdfSpinLockCreate(&attributes, &ctx->Lock);
    if (!NT_SUCCESS(status)) return status;
    WDF_TIMER_CONFIG_INIT_PERIODIC(&timer, Watchdog, 100);
    timer.AutomaticSerialization = FALSE;
    /* Periodic KMDF timers cannot run at PASSIVE_LEVEL. */
    attributes.ExecutionLevel = WdfExecutionLevelDispatch;
    status = WdfTimerCreate(&timer, &attributes, &ctx->Timer);
    if (!NT_SUCCESS(status)) return status;
    status = WdfDeviceCreateSymbolicLink(device, &link);
    if (!NT_SUCCESS(status)) return status;
    WDF_IO_QUEUE_CONFIG_INIT_DEFAULT_QUEUE(&queue, WdfIoQueueDispatchSequential);
    queue.EvtIoDeviceControl = Ioctl;
    return WdfIoQueueCreate(device, &queue, WDF_NO_OBJECT_ATTRIBUTES, NULL);
}

NTSTATUS DriverEntry(PDRIVER_OBJECT object, PUNICODE_STRING registry) {
    WDF_DRIVER_CONFIG config;
    C_ASSERT(sizeof(PTP_REPORT) == 35);
    WDF_DRIVER_CONFIG_INIT(&config, DeviceAdd);
    return WdfDriverCreate(object, registry, WDF_NO_OBJECT_ATTRIBUTES, &config, WDF_NO_HANDLE);
}
