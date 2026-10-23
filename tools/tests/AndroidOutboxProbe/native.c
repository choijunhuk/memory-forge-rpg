// Test-only libc boundary: Linux/Android flags on either Linux or macOS. Actual POSIX fds/locks/writes;
// never accepts a descriptor that did not originate in the production native open call.
#define _GNU_SOURCE
#include <sys/file.h>
#include <sys/stat.h>
#include <unistd.h>
#include <fcntl.h>
#include <errno.h>
#include <stddef.h>
#include <string.h>
#include <dlfcn.h>
static int owned[65536], private_mode[65536], fault_op, fault_errno, remaining, skip;
static int counts[7], premature_writes, flags_bad;
enum { OPEN=1, LOCK, MODE, WRITE, SYNC, CLOSE };
void mf_reset(void) { memset(counts,0,sizeof counts); fault_op=remaining=premature_writes=flags_bad=skip=0; }
void mf_fault(int op,int error,int count) { fault_op=op; fault_errno=error; remaining=count; skip=0; }
void mf_fault_after(int op,int error,int count,int before) { mf_fault(op,error,count); skip=before; }
int mf_count(int op) { return counts[op]; }
int mf_premature(void) { return premature_writes; }
int mf_flags_bad(void) { return flags_bad; }
static int inject(int op) { counts[op]++; if(op==fault_op && remaining>0) { if(skip>0) { skip--; return 0; } remaining--; errno=fault_errno; return 1; } return 0; }
int open(const char *path,int flags,...) {
    int (*real_open)(const char*,int,...)=dlsym(RTLD_NEXT,"open");
    if(inject(OPEN)) {
        int error=errno;
        if(error==EEXIST && (flags&128)) {
            int foreign=real_open(path,O_WRONLY|O_CREAT|O_EXCL,0600);
            if(foreign>=0) {
                ssize_t (*real_write)(int,const void*,size_t)=dlsym(RTLD_NEXT,"write");
                int (*real_close)(int)=dlsym(RTLD_NEXT,"close");
                real_write(foreign,"foreign-temp",12); real_close(foreign);
            }
        }
        errno=error; return -1;
    }
    int mapped;
    if(flags==(2|64|0x80000)) mapped=O_RDWR|O_CREAT|O_CLOEXEC;
    else if(flags==(1|64|128|0x80000)) mapped=O_WRONLY|O_CREAT|O_EXCL|O_CLOEXEC;
    else { flags_bad++; errno=EINVAL; return -1; }
    int fd=real_open(path,mapped,0600);
    if(fd>=0 && fd<65536) { owned[fd]=1; private_mode[fd]=0; }
    return fd;
}
int flock(int fd,int operation) {
    if(inject(LOCK)) return -1;
    if(fd<0 || fd>=65536 || !owned[fd]) { errno=EBADF; return -1; }
    int (*real_flock)(int,int)=dlsym(RTLD_NEXT,"flock");
    int result=real_flock(fd,operation);
    if(result<0 && errno==EWOULDBLOCK) errno=11;
    return result;
}
int fchmod(int fd,mode_t mode) {
    if(inject(MODE)) return -1;
    if(fd<0 || fd>=65536 || !owned[fd] || mode!=0600) { errno=EBADF; return -1; }
    int (*real_mode)(int,mode_t)=dlsym(RTLD_NEXT,"fchmod");
    int result=real_mode(fd,mode); if(result==0) private_mode[fd]=1; return result;
}
ssize_t write(int fd,const void *bytes,size_t length) {
    if(inject(WRITE)) return -1;
    if(fd<0 || fd>=65536 || !owned[fd]) { errno=EBADF; return -1; }
    if(!private_mode[fd]) { premature_writes++; errno=EACCES; return -1; }
    ssize_t (*real_write)(int,const void*,size_t)=dlsym(RTLD_NEXT,"write");
    return real_write(fd,bytes,length>17?17:length); // Force checked partial writes.
}
int fsync(int fd) {
    if(inject(SYNC)) return -1;
    if(fd<0 || fd>=65536 || !owned[fd]) { errno=EBADF; return -1; }
    int (*real_sync)(int)=dlsym(RTLD_NEXT,"fsync"); return real_sync(fd);
}
int close(int fd) {
    int fail=inject(CLOSE), error=errno;
    int (*real_close)(int)=dlsym(RTLD_NEXT,"close");
    int result=real_close(fd);
    if(fd>=0 && fd<65536) owned[fd]=private_mode[fd]=0;
    // Linux closes the fd even when close reports EINTR: retrying could close a reused descriptor.
    if(fail) { errno=error; return -1; } return result;
}
