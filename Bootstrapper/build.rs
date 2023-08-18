fn main() {
    //println!("cargo:rustc-link-arg-bins=/FILEALIGN:1");
    println!("cargo:rustc-link-arg-bins=/MERGE:.rdata=.text");
    println!("cargo:rustc-link-arg-bins=/MERGE:.pdata=.text");
    println!("cargo:rustc-link-arg-bins=/DEBUG:NONE");
    println!("cargo:rustc-link-arg-bins=/EMITPOGOPHASEINFO");
}