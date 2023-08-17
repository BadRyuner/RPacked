#![feature(vec_into_raw_parts)]

use std::env::args;
use std::io::Read;
use std::{fs, mem, ptr, slice};
use std::path::PathBuf;
use std::str::FromStr;

use netcorehost::bindings::hostfxr::{hostfxr_delegate_type, load_assembly_bytes_fn};
use netcorehost::{nethost, pdcstr};
use netcorehost::pdcstring::{PdCString, PdCStr};
use widestring::U16String;

fn main() {
    let hostfxr = nethost::load_hostfxr().unwrap();
    let rel_path = PathBuf::from("./bootstrapper.runtimeconfig.json");
    let abs_path = fs::canonicalize(rel_path).unwrap();
    let path = PdCString::from_str(abs_path.to_str().unwrap()).unwrap();
    let context = hostfxr.initialize_for_runtime_config(path).unwrap();
    
    let brotli_data = unsafe { BYTES.add(4) };
    let brotli_size = unsafe { *(BYTES as *const u32) } as usize;
    let mut data = unsafe {
        slice::from_raw_parts(brotli_data, brotli_size)
    };

    let mut decompressor = brotli_decompressor::Decompressor::new(&mut data, 65555);
    //brotli_decompressor::BrotliDecompress(data, w)
    let mut buf = Vec::<u8>::new();
    decompressor.read_to_end(&mut buf).unwrap();
    let raw_buf = buf.into_raw_parts();
    let pbytes = raw_buf.0;

    #[cfg(debug_assertions)]
    println!("Decompressed {} bytes from compressed {} bytes", raw_buf.1, brotli_size);

    let mut offset = 0;

    let entry_name_size = unsafe { *(pbytes.add(offset) as *const u32) }; offset += 4;
    let entry_name = unsafe { pbytes.add(offset) as *const u16 }; offset += entry_name_size as usize;
    let entry_name_str = unsafe { PdCStr::from_str_ptr(entry_name) };

    let size = unsafe { mem::transmute(*(pbytes.add(offset) as *const u32) as u64) }; offset += 4;

    let asm_code = unsafe { pbytes.add(offset) as *const u8 }; offset += size;
    
    let loader_ptr = context.get_runtime_delegate(hostfxr_delegate_type::hdt_load_assembly_bytes).unwrap();
    let loader : load_assembly_bytes_fn = unsafe { mem::transmute(loader_ptr) };

    unsafe {
        loader(asm_code, size, ptr::null_mut(), 0, ptr::null_mut(), ptr::null_mut());
    }

    #[cfg(debug_assertions)]
    println!("{:?} - {}", entry_name_str, size);

    load_dependencies(offset, pbytes, loader);

    let delegate_loader = context.get_delegate_loader().unwrap();
    let entry = delegate_loader.get_function_with_default_signature(entry_name_str, pdcstr!("NativeMain")).unwrap();
    
    let args : Vec<U16String> = args()
        .skip(1)
        .map(|a| widestring::U16String::from_str(a.as_str()))
        .collect();

    let a = args.into_raw_parts();

    #[cfg(debug_assertions)]
    println!("calling entry point");

    unsafe {
        entry(mem::transmute(a.0), a.1);
    }
}

#[inline(never)]
fn load_dependencies(_offset: usize, paylod: *const u8, loader : load_assembly_bytes_fn) {
    let mut offset = _offset; 

    #[cfg(debug_assertions)]
    println!("loading dependencies");
    let mut dlls_count = unsafe { *(paylod.add(offset) as *const u32) }; offset += 4;

    #[cfg(debug_assertions)]
    println!("dependencies count {}", dlls_count);

    while dlls_count > 0 {
        dlls_count -= 1;
        let size = unsafe { mem::transmute(*(paylod.add(offset) as *const u32) as u64) }; offset += 4;
        let asm_code = unsafe { paylod.add(offset) as *const u8 }; offset += size as usize;
        unsafe { loader(asm_code, size, ptr::null_mut(), 0, ptr::null_mut(), ptr::null_mut()); };

        #[cfg(debug_assertions)]
        println!("loaded {} - {}", dlls_count, size)
    }
}

#[no_mangle]
#[used]
static mut BYTES : *const u8 = {
    unsafe {
        mem::transmute(&NO_INLINE_DUDE)
    }
};

#[link_section = ".kek"]
#[used]
#[no_mangle]
static mut NO_INLINE_DUDE : u8 = 1;