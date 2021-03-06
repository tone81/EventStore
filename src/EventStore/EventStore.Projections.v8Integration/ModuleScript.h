#pragma once
#include "js1.h"
#include "CompiledScript.h"
#include "PreludeScript.h"

namespace js1 {

	class ModuleScript : public CompiledScript 
	{
	public:
		ModuleScript(PreludeScript *prelude_) :
			isolate(v8::Isolate::GetCurrent()), prelude(prelude_) 
		{
			isolate_add_ref(isolate);
		};

		virtual ~ModuleScript();

		Status compile_script(const uint16_t *module_source, const uint16_t *module_file_name);
		Status try_run();

		v8::Handle<v8::Object> get_module_object();


	protected:
		virtual v8::Isolate *get_isolate();
		virtual Status create_global_template(v8::Persistent<v8::ObjectTemplate> &result);

	private:
		v8::Isolate *isolate;
		PreludeScript *prelude;
		v8::Persistent<v8::Object> module_object;
	};


}