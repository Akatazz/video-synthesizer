#include "synth.hpp"
#include <gtk/gtk.h>
#include <fstream>
#include <memory>

struct State {
    Parameters parameters;
    SynthEngine engine {480, 270};
    GtkWidget *window = nullptr, *preview = nullptr, *status = nullptr;
    std::array<GtkWidget*, 9> sliders {};
    bool paused = false, reset = true, smoke = false;
    float time = 0;
    gint64 previous = 0, lastFrame = 0;
    int frames = 0;
};

static void showError(State* state, const std::string& error) {
    gtk_label_set_text(GTK_LABEL(state->status), error.c_str());
}

struct FileRequest {
    GtkWindow* window;
    bool saving;
    ~FileRequest() { g_object_unref(window); }
};

static void fileResponse(GObject* source, GAsyncResult* result, gpointer data) {
    std::unique_ptr<FileRequest> request(static_cast<FileRequest*>(data));
    auto* state = static_cast<State*>(g_object_get_data(G_OBJECT(request->window), "synth-state"));
    GError* error = nullptr;
    auto* dialog = GTK_FILE_DIALOG(source);
    GFile* file = request->saving ? gtk_file_dialog_save_finish(dialog, result, &error)
                                  : gtk_file_dialog_open_finish(dialog, result, &error);
    if (file) {
        char* path = file ? g_file_get_path(file) : nullptr;
        try {
            if (!path) throw std::runtime_error("Choose a local preset file.");
            bool saving = request->saving;
            if (saving) {
                std::ofstream output(path);
                output << savePreset(state->parameters).dump(2) << '\n';
                if (!output) throw std::runtime_error("Could not save preset.");
            } else {
                std::ifstream input(path);
                nlohmann::json json;
                input >> json;
                state->parameters = loadPreset(json);
                for (std::size_t i = 0; i < fields.size(); ++i)
                    gtk_range_set_value(GTK_RANGE(state->sliders[i]), state->parameters.*fields[i].member);
                state->reset = true;
            }
            gtk_label_set_text(GTK_LABEL(state->status), saving ? "Preset saved" : "Preset loaded");
        } catch (const std::exception& error) { showError(state, error.what()); }
        g_free(path);
        g_object_unref(file);
    }
    if (error) {
        if (!g_error_matches(error, GTK_DIALOG_ERROR, GTK_DIALOG_ERROR_DISMISSED) &&
            !g_error_matches(error, GTK_DIALOG_ERROR, GTK_DIALOG_ERROR_CANCELLED))
            showError(state, error->message);
        g_error_free(error);
    }
}

static void chooseFile(State* state, bool saving) {
    auto* dialog = gtk_file_dialog_new();
    gtk_file_dialog_set_title(dialog, saving ? "Save preset" : "Open preset");
    auto* filter = gtk_file_filter_new();
    gtk_file_filter_set_name(filter, "Synth presets (*.json)");
    gtk_file_filter_add_pattern(filter, "*.json");
    auto* filters = g_list_store_new(GTK_TYPE_FILE_FILTER);
    g_list_store_append(filters, filter);
    gtk_file_dialog_set_filters(dialog, G_LIST_MODEL(filters));
    g_object_unref(filters);
    g_object_unref(filter);
    if (saving) gtk_file_dialog_set_initial_name(dialog, "synth-preset.json");
    auto* request = new FileRequest {GTK_WINDOW(g_object_ref(state->window)), saving};
    if (saving) gtk_file_dialog_save(dialog, request->window, nullptr, fileResponse, request);
    else gtk_file_dialog_open(dialog, request->window, nullptr, fileResponse, request);
    g_object_unref(dialog);
}

static void draw(GtkDrawingArea*, cairo_t* context, int width, int height, gpointer data) {
    auto* state = static_cast<State*>(data);
    auto* surface = cairo_image_surface_create_for_data(reinterpret_cast<unsigned char*>(state->engine.pixels.data()),
        CAIRO_FORMAT_ARGB32, 480, 270, 480 * 4);
    double scale = std::min(width / 480.0, height / 270.0);
    cairo_set_source_rgb(context, 0, 0, 0);
    cairo_paint(context);
    cairo_translate(context, (width - 480 * scale) / 2, (height - 270 * scale) / 2);
    cairo_scale(context, scale, scale);
    cairo_set_source_surface(context, surface, 0, 0);
    cairo_paint(context);
    cairo_surface_destroy(surface);
}

static gboolean tick(GtkWidget*, GdkFrameClock* clock, gpointer data) {
    auto* state = static_cast<State*>(data);
    gint64 now = gdk_frame_clock_get_frame_time(clock);
    if (now - state->lastFrame < 33333) return G_SOURCE_CONTINUE;
    float delta = state->previous ? std::min((now - state->previous) / 1e6f, 0.1f) : 0;
    state->previous = now;
    state->lastFrame = now;
    if (state->paused && !state->reset) return G_SOURCE_CONTINUE;
    if (state->reset) { state->engine.reset(); state->time = 0; state->reset = false; }
    else state->time += delta;
    try { state->engine.render(state->time, state->parameters); }
    catch (const std::exception& error) { showError(state, error.what()); return G_SOURCE_REMOVE; }
    gtk_widget_queue_draw(state->preview);
    if (state->smoke && ++state->frames == 3)
        g_application_quit(G_APPLICATION(gtk_window_get_application(GTK_WINDOW(state->window))));
    return G_SOURCE_CONTINUE;
}

static void activate(GtkApplication* app, gpointer smoke) {
    auto* state = new State;
    state->smoke = GPOINTER_TO_INT(smoke);
    state->window = gtk_application_window_new(app);
    g_object_set_data_full(G_OBJECT(state->window), "synth-state", state, [](gpointer data) { delete static_cast<State*>(data); });
    gtk_window_set_title(GTK_WINDOW(state->window), "LZX-V · Video Synthesizer");
    gtk_window_set_default_size(GTK_WINDOW(state->window), 1100, 720);
    auto* root = gtk_box_new(GTK_ORIENTATION_VERTICAL, 16);
    for (auto side : {GTK_POS_LEFT, GTK_POS_RIGHT, GTK_POS_TOP, GTK_POS_BOTTOM}) {
        if (side == GTK_POS_LEFT) gtk_widget_set_margin_start(root, 24);
        if (side == GTK_POS_RIGHT) gtk_widget_set_margin_end(root, 24);
        if (side == GTK_POS_TOP) gtk_widget_set_margin_top(root, 24);
        if (side == GTK_POS_BOTTOM) gtk_widget_set_margin_bottom(root, 24);
    }
    gtk_window_set_child(GTK_WINDOW(state->window), root);
    auto* heading = gtk_label_new("LZX-V · MODULAR VIDEO SYNTHESIZER");
    gtk_label_set_xalign(GTK_LABEL(heading), 0);
    gtk_widget_add_css_class(heading, "title-1");
    gtk_box_append(GTK_BOX(root), heading);
    auto* actions = gtk_box_new(GTK_ORIENTATION_HORIZONTAL, 8);
    gtk_box_append(GTK_BOX(root), actions);
    auto addButton = [&](const char* title, GCallback callback) {
        auto* button = gtk_button_new_with_label(title);
        g_signal_connect(button, "clicked", callback, state);
        gtk_box_append(GTK_BOX(actions), button);
    };
    addButton("Pause", G_CALLBACK(+[](GtkButton* button, gpointer data) {
        auto* s = static_cast<State*>(data); s->paused = !s->paused;
        gtk_button_set_label(button, s->paused ? "Resume" : "Pause");
    }));
    addButton("Reset feedback", G_CALLBACK(+[](GtkButton*, gpointer data) { static_cast<State*>(data)->reset = true; }));
    addButton("Open preset…", G_CALLBACK(+[](GtkButton*, gpointer data) { chooseFile(static_cast<State*>(data), false); }));
    addButton("Save preset…", G_CALLBACK(+[](GtkButton*, gpointer data) { chooseFile(static_cast<State*>(data), true); }));
    auto* body = gtk_box_new(GTK_ORIENTATION_HORIZONTAL, 24);
    gtk_widget_set_vexpand(body, true);
    gtk_box_append(GTK_BOX(root), body);
    state->preview = gtk_drawing_area_new();
    gtk_widget_set_hexpand(state->preview, true);
    gtk_drawing_area_set_draw_func(GTK_DRAWING_AREA(state->preview), draw, state, nullptr);
    gtk_box_append(GTK_BOX(body), state->preview);
    auto* scroll = gtk_scrolled_window_new();
    gtk_widget_set_size_request(scroll, 280, -1);
    gtk_box_append(GTK_BOX(body), scroll);
    auto* controls = gtk_box_new(GTK_ORIENTATION_VERTICAL, 8);
    gtk_scrolled_window_set_child(GTK_SCROLLED_WINDOW(scroll), controls);
    for (std::size_t i = 0; i < fields.size(); ++i) {
        const auto& f = fields[i];
        auto* label = gtk_label_new(f.label);
        gtk_label_set_xalign(GTK_LABEL(label), 0);
        gtk_box_append(GTK_BOX(controls), label);
        auto* scale = gtk_scale_new_with_range(GTK_ORIENTATION_HORIZONTAL, f.min, f.max, (f.max - f.min) / 1000);
        state->sliders[i] = scale;
        gtk_scale_set_digits(GTK_SCALE(scale), 3);
        gtk_scale_set_draw_value(GTK_SCALE(scale), true);
        gtk_range_set_value(GTK_RANGE(scale), state->parameters.*f.member);
        g_object_set_data(G_OBJECT(scale), "field-index", GINT_TO_POINTER(i));
        g_signal_connect(scale, "value-changed", G_CALLBACK(+[](GtkRange* range, gpointer data) {
            auto* s = static_cast<State*>(data);
            int index = GPOINTER_TO_INT(g_object_get_data(G_OBJECT(range), "field-index"));
            s->parameters.*fields[index].member = float(gtk_range_get_value(range));
        }), state);
        gtk_box_append(GTK_BOX(controls), scale);
    }
    state->status = gtk_label_new("Linux native preview · 480 × 270");
    gtk_label_set_xalign(GTK_LABEL(state->status), 0);
    gtk_label_set_wrap(GTK_LABEL(state->status), true);
    gtk_box_append(GTK_BOX(root), state->status);
    gtk_widget_add_tick_callback(state->window, tick, state, nullptr);
    gtk_window_present(GTK_WINDOW(state->window));
}

int main(int argc, char** argv) {
    bool smoke = argc > 1 && std::string(argv[1]) == "--smoke-test";
    auto* app = gtk_application_new("com.akatazz.videosynthesizer", G_APPLICATION_DEFAULT_FLAGS);
    g_signal_connect(app, "activate", G_CALLBACK(activate), GINT_TO_POINTER(smoke));
    int result = g_application_run(G_APPLICATION(app), smoke ? 1 : argc, argv);
    g_object_unref(app);
    return result;
}
